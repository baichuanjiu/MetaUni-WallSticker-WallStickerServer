namespace Sticker.API.ReusableClass
{
    public class ResponseT<T>
    {
        public ResponseT(int code, string message)
        {
            Code = code;
            Message = message;
        }

        public ResponseT(int code, string message, T? data)
        {
            Code = code;
            Message = message;
            Data = data;
        }

        public int Code { get; set; }
        public string Message { get; set; }
        public T? Data { get; set; }
    }
}
