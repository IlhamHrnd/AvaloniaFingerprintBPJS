using System;
using System.Collections.Generic;

namespace AvaloniaFingerprint.Exceptions;

public class FaceRecognitionExceptions : Exception
{
    public FaceRecognitionExceptions(string message) : base(message)
    {
    }

    public FaceRecognitionExceptions(string message, Exception innerException) : base(message, innerException)
    {
    }
}

public class FaceModelMissingExceptions : FaceRecognitionExceptions
{
    public FaceModelMissingExceptions(string message) : base(message)
    {
    }

    public FaceModelMissingExceptions(string message, Exception innerException) : base(message, innerException)
    {
    }
}

public class NoFaceDetectedExceptions(string message) : FaceRecognitionExceptions(message)
{
}

public class MultipleFacesDetectedExceptions(string message) : FaceRecognitionExceptions(message)
{
    public int FaceCount { get; init; }
}

public class FaceEncodingExceptions : FaceRecognitionExceptions
{
    public FaceEncodingExceptions(string message) : base(message)
    {
    }

    public FaceEncodingExceptions(string message, Exception innerException) : base(message, innerException)
    {
    }
}

public class FaceImageExceptions : FaceRecognitionExceptions
{
    public FaceImageExceptions(string message) : base(message)
    {
    }

    public FaceImageExceptions(string message, Exception innerException) : base(message, innerException)
    {
    }
}
