namespace SmartAttendance.Application.Authentication;

public sealed class InvalidFirebaseIdTokenException : Exception
{
    public InvalidFirebaseIdTokenException(string message)
        : base(message)
    {
    }

    public InvalidFirebaseIdTokenException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class FirebaseServiceUnavailableException : Exception
{
    public FirebaseServiceUnavailableException(string message)
        : base(message)
    {
    }

    public FirebaseServiceUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
