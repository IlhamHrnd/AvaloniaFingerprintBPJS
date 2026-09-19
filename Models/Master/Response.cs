namespace AvaloniaFingerprint.Models.Master
{
    public class Response
    {
        public class Root<T>
        {
            public T? data { get; set; }
            public bool succeeded { get; set; } = false;
            public string[] errors { get; set; } = [];
            public string message { get; set; } = string.Empty;
        }
    }
}
