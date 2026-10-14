namespace Ebi.Alas.Api.Infrastructure.Email;

public sealed class EmailSendException(string message, Exception? innerException = null)
    : Exception(message, innerException);
