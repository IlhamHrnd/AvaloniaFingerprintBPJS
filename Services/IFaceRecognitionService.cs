using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaFingerprint.Services;

public interface IFaceRecognitionService : IDisposable
{
    bool IsReady { get; }

    string ModelDirectory { get; }

    Task<List<double>> GetFaceEncodingAsync(byte[] encodedImage, CancellationToken cancellationToken = default);

    Task<List<double>> GetFaceEncodingAsync(byte[] bgraPixels, int width, int height, int stride, CancellationToken cancellationToken = default);

    Task<List<double>> GetFaceEncodingFromFileAsync(string filePath, CancellationToken cancellationToken = default);
}
