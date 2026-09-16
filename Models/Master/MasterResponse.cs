namespace AvaloniaFingerprint.Models.Master
{
    public class MasterResponse<T>
    {
        public MasterResponse()
        {
        }
        public MasterResponse(T data)
        {
            errors = null;
        }
        public T data { get; set; }
        public bool succeeded => data != null;
        public string[]? errors { get; set; }
        public string message { get; set; }
    }
}
