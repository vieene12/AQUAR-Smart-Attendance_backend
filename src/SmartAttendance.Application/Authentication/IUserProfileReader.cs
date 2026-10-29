namespace SmartAttendance.Application.Authentication;

public interface IUserProfileReader
{
    Task<UserProfile?> FindByUidAsync(string uid, CancellationToken cancellationToken);
}
