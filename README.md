# Smart Attendance Backend

ASP.NET Core 10 REST API sử dụng Firebase Authentication và Cloud Firestore.

## Chạy local bằng PowerShell

Từ thư mục `backend`:

```powershell
dotnet restore SmartAttendance.slnx
dotnet build SmartAttendance.slnx --no-restore
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = 'http://localhost:8080'
$env:Firebase__ProjectId = 'your-firebase-project-id'
gcloud auth application-default login
dotnet run --project src/SmartAttendance.Api --no-build
```

`GET /api/health` là public và không khởi tạo Firebase. Swagger UI chỉ có trong
Development tại `http://localhost:8080/swagger`. Nút **Authorize** nhận Firebase
ID Token, không phải UID hoặc custom token.

## Emulator

Không dùng tài khoản hay dữ liệu Firebase thật khi kiểm thử:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:Firebase__ProjectId = 'demo-smart-attendance'
$env:FIREBASE_AUTH_EMULATOR_HOST = '127.0.0.1:9099'
$env:FIRESTORE_EMULATOR_HOST = '127.0.0.1:8081'
dotnet run --project src/SmartAttendance.Api --no-build
```

Các biến emulator phải được đặt trước khi process khởi động. Dùng project ID có
tiền tố `demo-` để tránh vô tình truy cập tài nguyên production.

## Tests và quality gates

```powershell
dotnet test SmartAttendance.slnx --no-restore
dotnet format SmartAttendance.slnx --verify-no-changes --no-restore
```

Integration tests hiện dùng fake token verifier và fake profile reader; chúng
không yêu cầu ADC và không kết nối Firebase thật.

### Firebase Emulator E2E

Từ thư mục gốc repository, chạy bằng PowerShell:

```powershell
.\backend\tests\run-firebase-emulator-tests.ps1
```

Script chỉ dùng project `demo-smart-attendance`, Auth Emulator tại
`127.0.0.1:9099`, Firestore Emulator tại `127.0.0.1:8081`, và JDK 22 cho riêng
phiên chạy. Test fixture Firestore dùng `deny all`; backend truy cập bằng Admin
SDK, không dựa vào quyền client. Không cần ADC và không ghi Firebase production.

Auth Emulator xác minh được token và mô phỏng việc revoke refresh token với
`checkRevoked=true`. Tuy nhiên, phiên bản Emulator hiện dùng không từ chối ID
token đã cấp trước khi tài khoản bị disable; test tương ứng được đánh dấu skip
như một giới hạn đã biết và phải được kiểm chứng lại trên Firebase staging.

## F02 Accounts API

Các endpoint sau đều yêu cầu Firebase ID Token của Admin có profile Firestore
`role=admin`, `status=active`:

| Method | Endpoint | Chức năng |
|---|---|---|
| `GET` | `/api/accounts?pageSize=20&pageToken=...` | Danh sách Admin/Employee/Manager, tối đa 100 bản ghi mỗi trang. |
| `POST` | `/api/accounts` | Tạo Firebase Auth account và profile `users/{uid}`. |
| `PUT` | `/api/accounts/{uid}` | Chỉ cập nhật `fullName` và `hourlyRate`. |
| `PATCH` | `/api/accounts/{uid}/status` | Đồng bộ `active/locked` với `Auth.Disabled`. |

Admin trong kết quả GET là chỉ đọc và có thể có `fullName`, `email` hoặc
`hourlyRate` bằng `null` với profile legacy. Không endpoint nào cho phép tạo/sửa role Admin, đổi email, nhận UID người thao
tác từ body hoặc trả password/token. Nếu tạo Auth thành công nhưng chưa xác nhận
được profile Firestore, backend disable Auth account để fail closed và yêu cầu
đối soát; backend không tự xóa tài khoản.

Trong Development, mở `/swagger`, bấm **Authorize** và nhập Firebase ID Token
không kèm UID/custom token. Ví dụ PowerShell:

```powershell
$headers = @{ Authorization = "Bearer $firebaseIdToken" }
Invoke-RestMethod `
  -Uri 'http://localhost:8080/api/accounts?pageSize=20' `
  -Headers $headers
```

## F04 Report Email API

`POST /api/employees/{uid}/attendance-reports/email` requires an active Admin
or Manager Firebase ID Token and a canonical UUID in `Idempotency-Key`. The
request body contains only `periodKind` (`week/month/quarter/year`) and
`anchorDate` (`yyyy-MM-dd`). The backend reads the active employee profile and
registered email from Firestore, builds the PDF, and sends it through Resend.

`Email__Enabled` defaults to `false`. Emulator tests enable the guarded Fake
mode, so they never call Resend or send real email. Never put a Resend API key
in source, appsettings, logs, the Docker image, or Git.

### Development Fake email mode

Để Flutter `emutest` nhận HTTP 202 mà không gửi email thật, khởi động Backend
local với toàn bộ cấu hình sau trong cùng một phiên PowerShell:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = 'http://127.0.0.1:8080'
$env:Firebase__ProjectId = 'demo-smart-attendance'
$env:FIREBASE_AUTH_EMULATOR_HOST = '127.0.0.1:9099'
$env:FIRESTORE_EMULATOR_HOST = '127.0.0.1:8081'
$env:Email__Mode = 'Fake'
$env:Email__Enabled = 'true'
Remove-Item Env:Email__ApiKey -ErrorAction SilentlyContinue
Remove-Item Env:GOOGLE_APPLICATION_CREDENTIALS -ErrorAction SilentlyContinue

dotnet run --project src/SmartAttendance.Api --no-build --no-restore
```

Fake sender chỉ tồn tại trong RAM, giữ request ID, người nhận, kỳ báo cáo và
bản sao byte PDF để integration tests xác minh. Nó không tạo `HttpClient` cho
Resend và không gửi email. Backend fail-fast nếu Fake mode chạy ngoài
Development, dùng project khác `demo-smart-attendance`, sai host Emulator,
`Email__Enabled` không phải `true`, hoặc phát hiện provider key/service-account
path. Dừng process sẽ xóa toàn bộ capture trong RAM.

Không đặt `Email__Mode=Fake` trong staging/production. Chế độ mặc định vẫn là
`Resend` với `Email__Enabled=false`.

## Docker

```powershell
docker build -t smart-attendance-api:local .
docker run --rm -p 8080:8080 `
  -e Firebase__ProjectId=your-firebase-project-id `
  smart-attendance-api:local
```

Container chạy non-root, mặc định nghe cổng 8080 và hỗ trợ biến `PORT` của Cloud Run.

## Biến môi trường

| Tên | Mục đích |
|---|---|
| `Firebase__ProjectId` | Firebase project ID; bắt buộc với endpoint cần auth/profile. |
| `GOOGLE_APPLICATION_CREDENTIALS` | Chỉ local/VPS: đường dẫn credential ngoài repository. |
| `FIREBASE_AUTH_EMULATOR_HOST` | Auth Emulator, ví dụ `127.0.0.1:9099`; không có scheme. |
| `FIRESTORE_EMULATOR_HOST` | Firestore Emulator, ví dụ `127.0.0.1:8081`; không có scheme. |
| `ASPNETCORE_ENVIRONMENT` | `Development`, `Test`, `Staging` hoặc `Production`. |
| `ASPNETCORE_URLS` | URL local của Kestrel. |
| `PORT` | Cổng do Cloud Run cung cấp khi `ASPNETCORE_URLS` không đặt. |
| `Email__Enabled` | Must be `true` to permit Resend HTTPS calls; defaults to `false`. |
| `Email__Mode` | `Resend` mặc định; `Fake` chỉ được phép với Development + Firebase Emulator demo. |
| `Email__ApiKey` | Resend API key from environment or Secret Manager only. |
| `Email__FromAddress` | Verified sender address configured in Resend. |
| `Email__FromName` | Sender display name; defaults to `Smart Attendance`. |
| `Email__TimeoutSeconds` | Provider timeout, clamped to 1–60 seconds. |

Không lưu private key, service-account JSON, ID Token hoặc secret trong source,
appsettings, Docker image, test, tài liệu hay log.

## Chuẩn bị Cloud Run (chưa deploy)

1. Gán user-managed service account cho Cloud Run và cấp quyền tối thiểu cần đọc
   `users/{uid}` cùng quyền Firebase Authentication cần thiết.
2. Dùng service identity/ADC của Cloud Run; không đưa service-account key vào image.
3. Cấu hình `Firebase__ProjectId`, region gần Firestore, HTTPS do Cloud Run quản lý,
   budget alert và rollback revision.
4. Kiểm thử Emulator, sau đó kiểm thử staging Firebase project riêng trước production.
5. Không bật endpoint `/api/auth/me` và `/api/auth/admin-check` trong Production;
   code hiện chỉ map chúng ở Development/Test.
