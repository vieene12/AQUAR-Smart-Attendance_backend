param(
    [string]$JavaHome = 'C:\Program Files\Java\jdk-22'
)

$ErrorActionPreference = 'Stop'
$projectId = 'demo-smart-attendance'
$repositoryRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$javaExecutable = Join-Path $JavaHome 'bin\java.exe'

if (-not (Test-Path -LiteralPath $javaExecutable -PathType Leaf)) {
    throw "JDK is unavailable at $javaExecutable. Nothing was installed."
}

if (-not (Get-Command firebase.cmd -ErrorAction SilentlyContinue)) {
    throw 'firebase.cmd is unavailable. Nothing was installed.'
}

$env:JAVA_HOME = $JavaHome
$env:Path = "$JavaHome\bin;$env:Path"
$env:RUN_FIREBASE_EMULATOR_E2E = '1'
$env:Firebase__ProjectId = $projectId

$testCommand = 'dotnet test backend/SmartAttendance.slnx --no-restore ' +
    '--filter FullyQualifiedName~FirebaseEmulator'

Push-Location $repositoryRoot
try {
    & firebase.cmd emulators:exec `
        --config firebase.emulator.json `
        --only auth,firestore `
        --project $projectId `
        $testCommand

    if ($LASTEXITCODE -ne 0) {
        throw "Firebase Emulator tests failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}
