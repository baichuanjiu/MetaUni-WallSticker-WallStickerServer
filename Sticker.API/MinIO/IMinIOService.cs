namespace Sticker.API.MinIO
{
    public interface IMinIOService
    {
        //入参：图片名、图片流
        //返回值：存储是否成功
        Task<bool> UploadImageAsync(string imageName, Stream file);

        //入参：视频名、图片流
        //返回值：存储是否成功
        Task<bool> UploadVideoAsync(string videoName, Stream file);

        //入参：所有要删除的文件的路径组成的数组
        //返回值：删除是否成功
        Task<bool> DeleteFilesAsync(List<string> paths);
    }
}
