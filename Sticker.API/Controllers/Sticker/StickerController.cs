using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;
using Sticker.API.Filters;
using Sticker.API.MinIO;
using Sticker.API.Models.Sticker;
using Sticker.API.MongoDBServices.Sticker;
using Sticker.API.Protos.BriefUserInfo;
using Sticker.API.Protos.GeneralNotification;
using Sticker.API.Redis;
using Sticker.API.ReusableClass;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Sticker.API.Controllers.Sticker
{
    public class GetAnonymousInfoResponseData
    {
        public GetAnonymousInfoResponseData(string avatar, string nickname)
        {
            Avatar = avatar;
            Nickname = nickname;
        }

        public string Avatar { get; set; }
        public string Nickname { get; set; }
    }
    public class StickerDataForClient
    {
        public StickerDataForClient(Models.Sticker.Sticker sticker, ReusableClass.BriefUserInfo briefUserInfo, string? replyTo, bool isLiked, int likesNumber, int repliesNumber, double trendValue)
        {
            Id = sticker.Id!;
            BriefUserInfo = briefUserInfo;
            IsAnonymous = sticker.IsAnonymous;
            IsDeleted = sticker.IsDeleted;
            CreatedTime = sticker.CreatedTime;
            ReplyTo = replyTo;
            Text = sticker.Text;
            Tags = sticker.Tags;
            Medias = sticker.Medias;
            IsLiked = isLiked;
            LikesNumber = likesNumber;
            RepliesNumber = repliesNumber;
            TrendValue = trendValue;
        }

        public string Id { get; set; } //主键
        public ReusableClass.BriefUserInfo BriefUserInfo { get; set; } //发帖用户
        public bool IsAnonymous { get; set; } //是否匿名
        public bool IsDeleted { get; set; } //是否已被删除
        public DateTime CreatedTime { get; set; } //发帖时间
        public string? ReplyTo { get; set; } //回复对象，如果没有则为空
        public string Text { get; set; } //正文内容
        public List<string> Tags { get; set; } //Tags
        public List<MediaMetadata> Medias { get; set; } //媒体文件（图片或视频）
        public bool IsLiked { get; set; } = false; //是否已点赞此内容
        public int LikesNumber { get; set; } //点赞数
        public int RepliesNumber { get; set; } //回复数
        public double TrendValue { get; set; } //热度值
    }
    public class ChangeLikeStatusResponseData
    {
        public ChangeLikeStatusResponseData(bool isLiked, int likesNumber)
        {
            IsLiked = isLiked;
            LikesNumber = likesNumber;
        }

        public bool IsLiked { get; set; }
        public int LikesNumber { get; set; }
    }
    public class GetStickerResponseData
    {
        public GetStickerResponseData(StickerDataForClient sticker)
        {
            Sticker = sticker;
        }

        public StickerDataForClient Sticker { get; set; }
    }
    public class GetStickersResponseData
    {
        public GetStickersResponseData(List<StickerDataForClient> dataList)
        {
            DataList = dataList;
        }

        public List<StickerDataForClient> DataList { get; set; }
    }
    public class PostMediaMetadata
    {
        public PostMediaMetadata()
        {
        }

        public PostMediaMetadata(IFormFile file, double aspectRatio, IFormFile? previewImage, int? timeTotal)
        {
            File = file;
            AspectRatio = aspectRatio;
            PreviewImage = previewImage;
            TimeTotal = timeTotal;
        }

        public IFormFile File { get; set; }
        public double AspectRatio { get; set; }
        public IFormFile? PreviewImage { get; set; }
        public int? TimeTotal { get; set; }
    }
    public class PostStickerRequestData
    {
        public PostStickerRequestData()
        {
        }

        public PostStickerRequestData(bool isAnonymous, string? replyStickerId, string text, List<string> tags, List<PostMediaMetadata> medias)
        {
            IsAnonymous = isAnonymous;
            ReplyStickerId = replyStickerId;
            Text = text;
            Tags = tags;
            Medias = medias;
        }

        public bool IsAnonymous { get; set; }
        public string? ReplyStickerId { get; set; }
        public string Text { get; set; }
        public List<string>? Tags { get; set; }
        public List<PostMediaMetadata>? Medias { get; set; }
    }
    public class PostStickerResponseData
    {
        public PostStickerResponseData(string id)
        {
            Id = id;
        }

        public string Id { get; set; }
    }
    public class SearchStickersRequestData
    {
        public SearchStickersRequestData(string searchKey, DateTime? start, DateTime? end, string searchMode, int offset)
        {
            SearchKey = searchKey;
            Start = start;
            End = end;
            SearchMode = searchMode;
            Offset = offset;
        }

        public string SearchKey { get; set; }
        public DateTime? Start { get; set; }
        public DateTime? End { get; set; }
        public string SearchMode { get; set; }
        public int Offset { get; set; }
    }

    [ApiController]
    [Route("/sticker")]
    [ServiceFilter(typeof(JWTAuthFilterService), IsReusable = true)]
    public class StickerController : Controller
    {
        //依赖注入
        private readonly IConfiguration _configuration;
        private readonly ILogger<StickerController> _logger;
        private readonly StickerService _stickerService;
        private readonly RedisConnection _redisConnection;
        private readonly StickerMediasMinIOService _stickerMediasMinIOService;
        private readonly GetBriefUserInfo.GetBriefUserInfoClient _rpcUserClient;
        private readonly SendGeneralNotification.SendGeneralNotificationClient _rpcGeneralNotificationClient;
        private readonly TrendManager.TrendManager _trendManager;

        public StickerController(IConfiguration configuration, ILogger<StickerController> logger, StickerService stickerService, RedisConnection redisConnection, StickerMediasMinIOService stickerMediasMinIOService, GetBriefUserInfo.GetBriefUserInfoClient rpcUserClient, SendGeneralNotification.SendGeneralNotificationClient rpcGeneralNotificationClient, TrendManager.TrendManager trendManager)
        {
            _configuration = configuration;
            _logger = logger;
            _stickerService = stickerService;
            _redisConnection = redisConnection;
            _stickerMediasMinIOService = stickerMediasMinIOService;
            _rpcUserClient = rpcUserClient;
            _rpcGeneralNotificationClient = rpcGeneralNotificationClient;
            _trendManager = trendManager;
        }

        [HttpGet("anonymousInfo")]
        public IActionResult GetAnonymousInfo([FromHeader] string JWT, [FromHeader] int UUID)
        {
            ResponseT<GetAnonymousInfoResponseData> getAnonymousInfoSucceed = new(0, "获取成功", new GetAnonymousInfoResponseData(_configuration["AnonymousAvatarUrl"]!, _configuration["AnonymousNickname"]!));
            return Ok(getAnonymousInfoSucceed);
        }

        [HttpPost]
        public async Task<IActionResult> PostSticker([FromForm] PostStickerRequestData formData, [FromHeader] string JWT, [FromHeader] int UUID)
        {
            formData.Tags ??= new();
            formData.Medias ??= new();

            if (formData.ReplyStickerId != null && !await _stickerService.CheckStatusAsync(formData.ReplyStickerId))
            {
                _logger.LogWarning("Warning：用户[ {UUID} ]发布Sticker时失败，原因为用户正尝试对不存在或已被删除的Sticker进行回复，疑似正绕过前端进行操作。", UUID);
                ResponseT<string> postStickerFailed = new(2, "禁止对不存在或已被删除的贴贴进行操作");
                return Ok(postStickerFailed);
            }

            if (formData.Text.Length == 0)
            {
                _logger.LogWarning("Warning：用户[ {UUID} ]发布Sticker时失败，原因为用户发表的文字内容为空，疑似正绕过前端进行操作。", UUID);
                ResponseT<string> postStickerFailed = new(3, "贴贴失败，文字内容不能为空");
                return Ok(postStickerFailed);
            }

            if (formData.Medias.Count > 4)
            {
                _logger.LogWarning("Warning：用户[ {UUID} ]发布Sticker时失败，原因为用户上传了超过限制数量的文件，疑似正绕过前端进行操作。", UUID);
                ResponseT<string> postStickerFailed = new(4, "贴贴失败，上传文件数超过限制");
                return Ok(postStickerFailed);
            }

            for (int i = 0; i < formData.Medias.Count; i++)
            {
                if ((!formData.Medias[i].File.ContentType.Contains("image") && !formData.Medias[i].File.ContentType.Contains("video")) || (formData.Medias[i].File.ContentType.Contains("video") && (formData.Medias[i].PreviewImage == null || (formData.Medias[i].PreviewImage != null && !formData.Medias[i].PreviewImage!.ContentType.Contains("image")))))
                {
                    _logger.LogWarning("Warning：用户[ {UUID} ]发布Sticker时失败，原因为用户上传了图片或视频以外的媒体文件，疑似正绕过前端进行操作。", UUID);
                    ResponseT<string> postStickerFailed = new(5, "贴贴失败，禁止上传规定格式以外的文件");
                    return Ok(postStickerFailed);
                }
            }

            List<Task<bool>> tasks = new();
            List<MediaMetadata> medias = new();
            List<string> paths = new();
            for (int i = 0; i < formData.Medias.Count; i++)
            {
                if (formData.Medias[i].File.ContentType.Contains("image"))
                {
                    IFormFile file = formData.Medias[i].File;

                    string extension = Path.GetExtension(file.FileName);

                    Stream stream = file.OpenReadStream();

                    string timestamp = (DateTime.Now - new DateTime(1970, 1, 1, 0, 0, 0)).TotalMilliseconds.ToString().Replace(".", "");

                    string fileName = timestamp + extension;

                    paths.Add(fileName);

                    string url = _configuration["MinIO:StickerMediasURLPrefix"]! + fileName;

                    tasks.Add(_stickerMediasMinIOService.UploadImageAsync(fileName, stream));

                    medias.Add(new MediaMetadata("image", url, formData.Medias[i].AspectRatio, null, null));
                }
                else if (formData.Medias[i].File.ContentType.Contains("video"))
                {
                    IFormFile file = formData.Medias[i].File;

                    string extension = Path.GetExtension(file.FileName);

                    Stream stream = file.OpenReadStream();

                    string timestamp = (DateTime.Now - new DateTime(1970, 1, 1, 0, 0, 0)).TotalMilliseconds.ToString().Replace(".", "");

                    string fileName = timestamp + extension;

                    paths.Add(fileName);

                    string url = _configuration["MinIO:StickerMediasURLPrefix"]! + fileName;

                    tasks.Add(_stickerMediasMinIOService.UploadVideoAsync(fileName, stream));

                    IFormFile preview = formData.Medias[i].PreviewImage!;

                    string previewExtension = Path.GetExtension(preview.FileName);

                    Stream previewStream = preview.OpenReadStream();

                    string previewTimestamp = (DateTime.Now - new DateTime(1970, 1, 1, 0, 0, 0)).TotalMilliseconds.ToString().Replace(".", "");

                    string previewFileName = previewTimestamp + previewExtension;

                    paths.Add(previewFileName);

                    string previewURL = _configuration["MinIO:StickerMediasURLPrefix"]! + previewFileName;

                    tasks.Add(_stickerMediasMinIOService.UploadImageAsync(previewFileName, previewStream));

                    medias.Add(new MediaMetadata("video", url, formData.Medias[i].AspectRatio, previewURL, formData.Medias[i].TimeTotal));
                }
            }

            Task.WaitAll(tasks.ToArray());
            bool isStoreMediasSucceed = true;
            foreach (var task in tasks)
            {
                if (!task.Result)
                {
                    isStoreMediasSucceed = false;
                    break;
                }
            }
            if (!isStoreMediasSucceed)
            {
                _ = _stickerMediasMinIOService.DeleteFilesAsync(paths);
                _logger.LogWarning("Warning：用户[ {UUID} ]发布Sticker时失败，MinIO存储媒体文件时发生错误。", UUID);
                ResponseT<string> postStickerFailed = new(6, "发生错误，贴贴失败");
                return Ok(postStickerFailed);
            }

            List<string> timeLine = new();
            ReplyInfo? replyTo = null;
            if (formData.ReplyStickerId != null)
            {
                timeLine = (await _stickerService.GetTimeLineAsync(formData.ReplyStickerId))!;
                timeLine.Add(formData.ReplyStickerId);
                replyTo = (await _stickerService.GetReplyInfoAsync(formData.ReplyStickerId))!;
            }

            Models.Sticker.Sticker sticker = new(null, UUID, formData.IsAnonymous, false, DateTime.Now, timeLine, replyTo, formData.Text, formData.Tags, medias);
            try
            {
                await _stickerService.CreateAsync(sticker);
                if (formData.ReplyStickerId != null)
                {
                    IDatabase stickerRedis = _redisConnection.GetStickerDatabase();
                    stickerRedis.SetAdd($"{formData.ReplyStickerId}Replies", sticker.Id);
                    _ = _trendManager.ReplyAction(formData.ReplyStickerId, UUID);
                }
            }
            catch (Exception ex)
            {
                _ = _stickerMediasMinIOService.DeleteFilesAsync(paths);
                _logger.LogWarning("Warning：用户[ {UUID} ]发布Sticker时失败，将数据[ {sticker} ]存入数据库时发生错误，报错信息为[ {ex} ]。", UUID, sticker, ex);
                ResponseT<string> postStickerFailed = new(7, "发生错误，贴贴失败");
                return Ok(postStickerFailed);
            }

            if (replyTo != null && replyTo.UUID != UUID)
            {
                //向被回复者发送消息
                //发送RPC请求
                StringBuilder descriptionText = new();
                descriptionText.Append($"回复内容：{formData.Text}");
                medias.ForEach((m) =>
                {
                    descriptionText.Append("【媒体文件】");
                });

                SendGeneralNotificationSingleRequest request = new()
                {
                    UUID = replyTo.UUID,
                    Title = "墙贴收到了新的回复",
                    Description = descriptionText.ToString(),
                    MessageText = "墙贴收到了新的回复",
                    OpenPageUrl = $"/miniApps/wallSticker/stickerDetailsPage/{sticker.Id}",
                    OpenPageText = "查看详情",
                };

                _ = _rpcGeneralNotificationClient.SendGeneralNotificationSingleAsync(
                              request);
            }

            ResponseT<PostStickerResponseData> postStickerSucceed = new(0, "贴贴成功", new PostStickerResponseData(sticker.Id!));
            return Ok(postStickerSucceed);
        }

        [HttpGet("me/stickersNumber")]
        public async Task<IActionResult> GetMyStickersNumber([FromHeader] string JWT, [FromHeader] int UUID)
        {
            long stickersNumber = await _stickerService.GetMyStickersNumber(UUID);

            ResponseT<long> getMyStickersNumberSucceed = new(0, "获取成功", stickersNumber);
            return Ok(getMyStickersNumberSucceed);
        }

        [HttpGet("me/stickers/{lastDateTime?}/{lastId?}")]
        public async Task<IActionResult> GetMyStickers([FromRoute] DateTime? lastDateTime, [FromRoute] string? lastId, [FromHeader] string JWT, [FromHeader] int UUID)
        {
            List<Models.Sticker.Sticker> myStickers = await _stickerService.GetMyStickersByLastResultAsync(UUID, lastDateTime, lastId);

            GetStickersResponseData getStickersResponseData = new(await AssembleStickerData(myStickers, UUID));
            ResponseT<GetStickersResponseData> getStickersSucceed = new(0, "获取成功", getStickersResponseData);
            return Ok(getStickersSucceed);
        }

        [HttpDelete("{stickerId}")]
        public async Task<IActionResult> DeleteStickerById([FromRoute] string stickerId, [FromHeader] string JWT, [FromHeader] int UUID)
        {
            Models.Sticker.Sticker? sticker = await _stickerService.GetStickerByIdAsync(stickerId);

            if (sticker == null || sticker.IsDeleted)
            {
                _logger.LogWarning("Warning：用户[ {UUID} ]正在删除不存在或已被删除的Sticker[ {stickerId} ]。", UUID, stickerId);
                ResponseT<string> deleteStickerFailed = new(2, "您正在对一个不存在或已被删除的贴贴进行删除");
                return Ok(deleteStickerFailed);
            }

            if (sticker.UUID != UUID)
            {
                _logger.LogWarning("Warning：用户[ {UUID} ]正在删除一个不属于该用户的Sticker[ {stickerId} ]。", UUID, stickerId);
                ResponseT<string> deleteStickerFailed = new(3, "您正在对一个不属于您的贴贴进行删除");
                return Ok(deleteStickerFailed);
            }

            if (sticker.Medias.Count != 0)
            {
                List<string> paths = new();
                foreach (var medias in sticker.Medias)
                {
                    paths.Add(medias.URL.Replace(_configuration["MinIO:StickerMediasURLPrefix"]!, ""));
                }
                _ = _stickerMediasMinIOService.DeleteFilesAsync(paths);
            }

            _ = _stickerService.DeleteAsync(stickerId);

            ResponseT<bool> deleteStickerSucceed = new(0, "删除成功", true);
            return Ok(deleteStickerSucceed);
        }

        [HttpGet("{stickerId}")]
        public async Task<IActionResult> GetStickerById([FromRoute] string stickerId, [FromHeader] string JWT, [FromHeader] int UUID)
        {
            Models.Sticker.Sticker? sticker = await _stickerService.GetStickerByIdAsync(stickerId);

            if (sticker == null)
            {
                _logger.LogWarning("Warning：用户[ {UUID} ]正在查询不存在的Sticker[ {stickerId} ]的信息。", UUID, stickerId);
                ResponseT<string> getStickerFailed = new(2, "您正在对一个不存在的贴贴进行查询");
                return Ok(getStickerFailed);
            }

            _ = _trendManager.ReadAction(stickerId, UUID);
            GetStickerResponseData getStickerResponseData = new((await AssembleStickerData(new() { sticker }, UUID)).First());
            ResponseT<GetStickerResponseData> getStickerSucceed = new(0, "获取成功", getStickerResponseData);
            return Ok(getStickerSucceed);
        }

        [HttpPut("like/{stickerId}")]
        public async Task<IActionResult> ChangeLikeStatus([FromRoute] string stickerId, [FromHeader] string JWT, [FromHeader] int UUID)
        {
            if (!await _stickerService.CheckStatusAsync(stickerId))
            {
                _logger.LogWarning("Warning：用户[ {UUID} ]企图对不存在或已被删除的Sticker[ {stickerId} ]进行点赞或取消点赞操作。", UUID, stickerId);
                ResponseT<string> changeLikeStatusFailed = new(2, "您无法对一个不存在或已删除的Sticker进行操作");
                return Ok(changeLikeStatusFailed);
            }

            IDatabase stickerRedis = _redisConnection.GetStickerDatabase();
            ResponseT<ChangeLikeStatusResponseData> changeLikeStatusSucceed;
            if (stickerRedis.SetContains($"{stickerId}Likes", UUID))
            {
                await stickerRedis.SetRemoveAsync($"{stickerId}Likes", UUID);
                changeLikeStatusSucceed = new(0, "取消点赞成功", new ChangeLikeStatusResponseData(false, (int)await stickerRedis.SetLengthAsync($"{stickerId}Likes")));
                return Ok(changeLikeStatusSucceed);
            }

            _ = _trendManager.LikeAction(stickerId, UUID);
            await stickerRedis.SetAddAsync($"{stickerId}Likes", UUID);
            changeLikeStatusSucceed = new(0, "点赞成功", new ChangeLikeStatusResponseData(true, (int)await stickerRedis.SetLengthAsync($"{stickerId}Likes")));
            return Ok(changeLikeStatusSucceed);
        }

        [HttpGet("timeLine/{stickerId}/{offset}")]
        public async Task<IActionResult> GetTimeLineStickersByOffset([FromRoute] string stickerId, [FromRoute] int offset, [FromHeader] string JWT, [FromHeader] int UUID)
        {
            List<Models.Sticker.Sticker>? timeLine = await _stickerService.GetTimeLineStickersByOffsetAsync(stickerId, offset);

            if (timeLine == null)
            {
                _logger.LogWarning("Warning：用户[ {UUID} ]正在查询不存在的Sticker[ {stickerId} ]的TimeLine。", UUID, stickerId);
                ResponseT<string> getTimeLineFailed = new(2, "您正在对一个不存在的贴贴的时间线进行查询");
                return Ok(getTimeLineFailed);
            }

            GetStickersResponseData getStickersResponseData = new(await AssembleStickerData(timeLine, UUID));
            ResponseT<GetStickersResponseData> getTimeLineSucceed = new(0, "获取成功", getStickersResponseData);
            return Ok(getTimeLineSucceed);
        }

        [HttpGet("timeLine/replying/{stickerId}/{offset}")]
        public async Task<IActionResult> GetTimeLineStickersWhenReplyingByOffset([FromRoute] string stickerId, [FromRoute] int offset, [FromHeader] string JWT, [FromHeader] int UUID)
        {
            List<Models.Sticker.Sticker>? timeLine = await _stickerService.GetTimeLineStickersWhenReplyingByOffsetAsync(stickerId, offset);

            if (timeLine == null)
            {
                _logger.LogWarning("Warning：用户[ {UUID} ]正在查询不存在的Sticker[ {stickerId} ]的TimeLine。", UUID, stickerId);
                ResponseT<string> getTimeLineFailed = new(2, "您正在对一个不存在的贴贴的时间线进行查询");
                return Ok(getTimeLineFailed);
            }

            GetStickersResponseData getStickersResponseData = new(await AssembleStickerData(timeLine, UUID));
            ResponseT<GetStickersResponseData> getTimeLineSucceed = new(0, "获取成功", getStickersResponseData);
            return Ok(getTimeLineSucceed);
        }

        [HttpGet("replies/{stickerId}/{offset}")]
        public async Task<IActionResult> GetRepliesByOffset([FromRoute] string stickerId, [FromRoute] int offset, [FromHeader] string JWT, [FromHeader] int UUID)
        {
            IDatabase stickerRedis = _redisConnection.GetStickerDatabase();
            List<RedisValue> redisValueList = (await stickerRedis.SetMembersAsync($"{stickerId}Replies")).ToList();
            if (redisValueList.Count == 0)
            {
                GetStickersResponseData getStickersResponseData = new(new());
                ResponseT<GetStickersResponseData> getRepliesSucceed = new(0, "获取成功", getStickersResponseData);
                return Ok(getRepliesSucceed);
            }
            else
            {
                List<string> replyIdList = new();
                redisValueList.ForEach(value =>
                    {
                        replyIdList.Add((string)value!);
                    }
                    );

                List<Models.Sticker.Sticker> replies = await _stickerService.GetRepliesAsync(replyIdList, offset);

                GetStickersResponseData getStickersResponseData = new(await AssembleStickerData(replies, UUID));
                ResponseT<GetStickersResponseData> getRepliesSucceed = new(0, "获取成功", getStickersResponseData);
                return Ok(getRepliesSucceed);
            }
        }

        [HttpGet("today/{lastDateTime?}/{lastId?}")]
        public async Task<IActionResult> GetTodayStickers([FromRoute] DateTime? lastDateTime, [FromRoute] string? lastId, [FromHeader] string JWT, [FromHeader] int UUID)
        {
            List<Models.Sticker.Sticker> todayStickers = await _stickerService.GetTodayStickersByLastResultAsync(lastDateTime, lastId);

            GetStickersResponseData getStickersResponseData = new(await AssembleStickerData(todayStickers, UUID));
            ResponseT<GetStickersResponseData> getStickersSucceed = new(0, "获取成功", getStickersResponseData);
            return Ok(getStickersSucceed);
        }

        [HttpGet("trend/{rank}")]
        public async Task<IActionResult> GetTrendStickersByRank([FromRoute] int rank, [FromHeader] string JWT, [FromHeader] int UUID)
        {
            var stickerIdWithTrendValueList = await _trendManager.GetTrendRankWithTrendValueByRange(rank, rank + 20);
            var stickers = await _stickerService.GetStickersByIdListAsync(stickerIdWithTrendValueList.stickers);

            GetStickersResponseData getStickersResponseData = new(AssembleStickerDataWithTrendValues(stickers, stickerIdWithTrendValueList.trendValues, UUID));
            ResponseT<GetStickersResponseData> getStickersSucceed = new(0, "获取成功", getStickersResponseData);
            return Ok(getStickersSucceed);
        }

        [HttpPost("search")]
        public async Task<IActionResult> SearchStickers([FromBody] SearchStickersRequestData searchRequestData, [FromHeader] string JWT, [FromHeader] int UUID)
        {
            var searchKeys = Regex.Split(searchRequestData.SearchKey, " +").ToList();
            searchKeys.RemoveAll(key => key == "");

            if (searchKeys.Count == 0)
            {
                if (searchRequestData.Start != null && searchRequestData.End != null)
                {
                    if (searchRequestData.SearchMode == "all")
                    {
                        List<Models.Sticker.Sticker> stickers = await _stickerService.Search(start: (DateTime)searchRequestData.Start, end: (DateTime)searchRequestData.End, offset: searchRequestData.Offset);

                        GetStickersResponseData getStickersResponseData = new(await AssembleStickerData(stickers, UUID));
                        ResponseT<GetStickersResponseData> getStickersSucceed = new(0, "查询成功", getStickersResponseData);
                        return Ok(getStickersSucceed);
                    }
                    else if (searchRequestData.SearchMode == "me")
                    {
                        List<Models.Sticker.Sticker> stickers = await _stickerService.Search(start: (DateTime)searchRequestData.Start, end: (DateTime)searchRequestData.End, uuid: UUID, offset: searchRequestData.Offset);

                        GetStickersResponseData getStickersResponseData = new(await AssembleStickerData(stickers, UUID));
                        ResponseT<GetStickersResponseData> getStickersSucceed = new(0, "查询成功", getStickersResponseData);
                        return Ok(getStickersSucceed);
                    }
                }
            }
            else
            {
                if (searchRequestData.Start != null && searchRequestData.End != null)
                {
                    if (searchRequestData.SearchMode == "all")
                    {
                        List<Models.Sticker.Sticker> stickers = await _stickerService.Search(searchKeys: searchKeys, start: (DateTime)searchRequestData.Start, end: (DateTime)searchRequestData.End, offset: searchRequestData.Offset);

                        GetStickersResponseData getStickersResponseData = new(await AssembleStickerData(stickers, UUID));
                        ResponseT<GetStickersResponseData> getStickersSucceed = new(0, "查询成功", getStickersResponseData);
                        return Ok(getStickersSucceed);
                    }
                    else if (searchRequestData.SearchMode == "me")
                    {
                        List<Models.Sticker.Sticker> stickers = await _stickerService.Search(searchKeys: searchKeys, start: (DateTime)searchRequestData.Start, end: (DateTime)searchRequestData.End, uuid: UUID, offset: searchRequestData.Offset);

                        GetStickersResponseData getStickersResponseData = new(await AssembleStickerData(stickers, UUID));
                        ResponseT<GetStickersResponseData> getStickersSucceed = new(0, "查询成功", getStickersResponseData);
                        return Ok(getStickersSucceed);
                    }
                }
                else
                {
                    if (searchRequestData.SearchMode == "all")
                    {
                        List<Models.Sticker.Sticker> stickers = await _stickerService.Search(searchKeys: searchKeys, offset: searchRequestData.Offset);

                        GetStickersResponseData getStickersResponseData = new(await AssembleStickerData(stickers, UUID));
                        ResponseT<GetStickersResponseData> getStickersSucceed = new(0, "查询成功", getStickersResponseData);
                        return Ok(getStickersSucceed);
                    }
                    else if (searchRequestData.SearchMode == "me")
                    {
                        List<Models.Sticker.Sticker> stickers = await _stickerService.Search(searchKeys: searchKeys, uuid: UUID, offset: searchRequestData.Offset);

                        GetStickersResponseData getStickersResponseData = new(await AssembleStickerData(stickers, UUID));
                        ResponseT<GetStickersResponseData> getStickersSucceed = new(0, "查询成功", getStickersResponseData);
                        return Ok(getStickersSucceed);
                    }
                }
            }

            _logger.LogWarning("Warning：用户[ {UUID} ]在查询Stickers时传递了不合法的参数[ {searchRequestData} ]，疑似正绕过前端进行操作。", UUID, searchRequestData);
            ResponseT<string> searchStickersFailed = new(2, "查询失败，因为传递了不合法的参数");
            return Ok(searchStickersFailed);
        }

        private async Task<List<StickerDataForClient>> AssembleStickerData(List<Models.Sticker.Sticker> stickers, int uuid)
        {
            if (stickers.Count == 0)
            {
                return new();
            }

            IDatabase stickerRedis = _redisConnection.GetStickerDatabase();
            var stickerBatch = stickerRedis.CreateBatch();
            Dictionary<string, Task<long>> likesNumberDictionary = new();
            Dictionary<string, Task<bool>> isLikedDictionary = new();
            Dictionary<string, Task<long>> repliesNumberDictionary = new();

            IDatabase briefUserInfoRedis = _redisConnection.GetBriefUserInfoDatabase();
            var briefUserInfoBatch = briefUserInfoRedis.CreateBatch();
            Dictionary<int, Task<RedisValue>> briefUserInfoDictionary = new();

            stickers.ForEach(stickerData =>
            {
                likesNumberDictionary.Add(stickerData.Id!, stickerBatch.SetLengthAsync($"{stickerData.Id}Likes"));
                isLikedDictionary.Add(stickerData.Id!, stickerBatch.SetContainsAsync($"{stickerData.Id}Likes", uuid));
                repliesNumberDictionary.Add(stickerData.Id!, stickerBatch.SetLengthAsync($"{stickerData.Id}Replies"));

                if (!stickerData.IsAnonymous && !briefUserInfoDictionary.ContainsKey(stickerData.UUID))
                {
                    briefUserInfoDictionary.Add(stickerData.UUID, briefUserInfoBatch.StringGetAsync(stickerData.UUID.ToString()));
                }

                if (stickerData.ReplyTo != null && !stickerData.ReplyTo.IsAnonymous)
                {
                    if (!briefUserInfoDictionary.ContainsKey(stickerData.ReplyTo.UUID))
                    {
                        briefUserInfoDictionary.Add(stickerData.ReplyTo.UUID, briefUserInfoBatch.StringGetAsync(stickerData.ReplyTo.UUID.ToString()));
                    }
                }

            });
            stickerBatch.Execute();
            briefUserInfoBatch.Execute();

            stickerBatch.WaitAll(likesNumberDictionary.Values.ToArray());
            stickerBatch.WaitAll(isLikedDictionary.Values.ToArray());
            stickerBatch.WaitAll(repliesNumberDictionary.Values.ToArray());
            briefUserInfoBatch.WaitAll(briefUserInfoDictionary.Values.ToArray());

            GetBriefUserInfoMapRequest request = new();
            stickers.ForEach(stickerData =>
            {
                if (!stickerData.IsAnonymous && briefUserInfoDictionary[stickerData.UUID].Result == RedisValue.Null)
                {
                    request.QueryList.Add(stickerData.UUID);
                }

                if (stickerData.ReplyTo != null && !stickerData.ReplyTo.IsAnonymous)
                {
                    if (briefUserInfoDictionary[stickerData.ReplyTo.UUID].Result == RedisValue.Null)
                    {
                        request.QueryList.Add(stickerData.ReplyTo.UUID);
                    }
                }
            });
            Dictionary<int, ReusableClass.BriefUserInfo> briefUserInfoMap = new();
            if (request.QueryList.Count != 0)
            {
                GetBriefUserInfoMapReply reply = _rpcUserClient.GetBriefUserInfoMap(request);
                var briefUserInfoCacheBatch = briefUserInfoRedis.CreateBatch();

                foreach (KeyValuePair<int, Protos.BriefUserInfo.BriefUserInfo> entry in reply.BriefUserInfoMap)
                {
                    _ = briefUserInfoCacheBatch.StringSetAsync(entry.Key.ToString(), JsonSerializer.Serialize(new ReusableClass.BriefUserInfo(entry.Value)), TimeSpan.FromMinutes(15));
                    briefUserInfoMap.Add(entry.Key, new ReusableClass.BriefUserInfo(entry.Value));
                }

                briefUserInfoCacheBatch.Execute();
            }

            foreach (var entry in briefUserInfoDictionary)
            {
                if (entry.Value.Result != RedisValue.Null)
                {
                    briefUserInfoMap.Add(entry.Key, JsonSerializer.Deserialize<ReusableClass.BriefUserInfo>(entry.Value.Result.ToString())!);
                }
            }

            List<string> idList = new();
            stickers.ForEach((sticker) =>
            {
                idList.Add(sticker.Id!);
            });
            List<double> trendValues = await _trendManager.GetTrendValues(idList);

            List<StickerDataForClient> dataList = new();
            for (int i = 0; i < stickers.Count; i++)
            {
                ReusableClass.BriefUserInfo briefUserInfo;
                if (stickers[i].IsAnonymous)
                {
                    briefUserInfo = new(0, _configuration["AnonymousAvatarUrl"]!, _configuration["AnonymousNickname"]!);
                }
                else
                {
                    briefUserInfo = briefUserInfoMap[stickers[i].UUID];
                }
                string? replyTo;
                if (stickers[i].ReplyTo == null)
                {
                    replyTo = null;
                }
                else if (stickers[i].ReplyTo!.IsAnonymous)
                {
                    replyTo = _configuration["AnonymousNickname"]!;
                }
                else
                {
                    replyTo = briefUserInfoMap[stickers[i].ReplyTo!.UUID].Nickname;
                }
                dataList.Add(new StickerDataForClient(sticker: stickers[i], briefUserInfo: briefUserInfo, replyTo: replyTo, isLiked: isLikedDictionary[stickers[i].Id!].Result, likesNumber: (int)likesNumberDictionary[stickers[i].Id!].Result, repliesNumber: (int)repliesNumberDictionary[stickers[i].Id!].Result, trendValue: trendValues[i]));
            }

            return dataList;
        }

        private List<StickerDataForClient> AssembleStickerDataWithTrendValues(List<Models.Sticker.Sticker> stickers, List<double> trendValues, int uuid)
        {
            if (stickers.Count == 0)
            {
                return new();
            }

            IDatabase stickerRedis = _redisConnection.GetStickerDatabase();
            var stickerBatch = stickerRedis.CreateBatch();
            Dictionary<string, Task<long>> likesNumberDictionary = new();
            Dictionary<string, Task<bool>> isLikedDictionary = new();
            Dictionary<string, Task<long>> repliesNumberDictionary = new();

            IDatabase briefUserInfoRedis = _redisConnection.GetBriefUserInfoDatabase();
            var briefUserInfoBatch = briefUserInfoRedis.CreateBatch();
            Dictionary<int, Task<RedisValue>> briefUserInfoDictionary = new();

            stickers.ForEach(stickerData =>
            {
                likesNumberDictionary.Add(stickerData.Id!, stickerBatch.SetLengthAsync($"{stickerData.Id}Likes"));
                isLikedDictionary.Add(stickerData.Id!, stickerBatch.SetContainsAsync($"{stickerData.Id}Likes", uuid));
                repliesNumberDictionary.Add(stickerData.Id!, stickerBatch.SetLengthAsync($"{stickerData.Id}Replies"));

                if (!stickerData.IsAnonymous && !briefUserInfoDictionary.ContainsKey(stickerData.UUID))
                {
                    briefUserInfoDictionary.Add(stickerData.UUID, briefUserInfoBatch.StringGetAsync(stickerData.UUID.ToString()));
                }

                if (stickerData.ReplyTo != null && !stickerData.ReplyTo.IsAnonymous)
                {
                    if (!briefUserInfoDictionary.ContainsKey(stickerData.ReplyTo.UUID))
                    {
                        briefUserInfoDictionary.Add(stickerData.ReplyTo.UUID, briefUserInfoBatch.StringGetAsync(stickerData.ReplyTo.UUID.ToString()));
                    }
                }

            });
            stickerBatch.Execute();
            briefUserInfoBatch.Execute();

            stickerBatch.WaitAll(likesNumberDictionary.Values.ToArray());
            stickerBatch.WaitAll(isLikedDictionary.Values.ToArray());
            stickerBatch.WaitAll(repliesNumberDictionary.Values.ToArray());
            briefUserInfoBatch.WaitAll(briefUserInfoDictionary.Values.ToArray());

            GetBriefUserInfoMapRequest request = new();
            stickers.ForEach(stickerData =>
            {
                if (!stickerData.IsAnonymous && briefUserInfoDictionary[stickerData.UUID].Result == RedisValue.Null)
                {
                    request.QueryList.Add(stickerData.UUID);
                }

                if (stickerData.ReplyTo != null && !stickerData.ReplyTo.IsAnonymous)
                {
                    if (briefUserInfoDictionary[stickerData.ReplyTo.UUID].Result == RedisValue.Null)
                    {
                        request.QueryList.Add(stickerData.ReplyTo.UUID);
                    }
                }
            });
            Dictionary<int, ReusableClass.BriefUserInfo> briefUserInfoMap = new();
            if (request.QueryList.Count != 0)
            {
                GetBriefUserInfoMapReply reply = _rpcUserClient.GetBriefUserInfoMap(request);
                var briefUserInfoCacheBatch = briefUserInfoRedis.CreateBatch();

                foreach (KeyValuePair<int, Protos.BriefUserInfo.BriefUserInfo> entry in reply.BriefUserInfoMap)
                {
                    _ = briefUserInfoCacheBatch.StringSetAsync(entry.Key.ToString(), JsonSerializer.Serialize(new ReusableClass.BriefUserInfo(entry.Value)), TimeSpan.FromMinutes(15));
                    briefUserInfoMap.Add(entry.Key, new ReusableClass.BriefUserInfo(entry.Value));
                }

                briefUserInfoCacheBatch.Execute();
            }

            foreach (var entry in briefUserInfoDictionary)
            {
                if (entry.Value.Result != RedisValue.Null)
                {
                    briefUserInfoMap.Add(entry.Key, JsonSerializer.Deserialize<ReusableClass.BriefUserInfo>(entry.Value.Result.ToString())!);
                }
            }

            List<StickerDataForClient> dataList = new();
            for (int i = 0; i < stickers.Count; i++)
            {
                ReusableClass.BriefUserInfo briefUserInfo;
                if (stickers[i].IsAnonymous)
                {
                    briefUserInfo = new(0, _configuration["AnonymousAvatarUrl"]!, _configuration["AnonymousNickname"]!);
                }
                else
                {
                    briefUserInfo = briefUserInfoMap[stickers[i].UUID];
                }
                string? replyTo;
                if (stickers[i].ReplyTo == null)
                {
                    replyTo = null;
                }
                else if (stickers[i].ReplyTo!.IsAnonymous)
                {
                    replyTo = _configuration["AnonymousNickname"]!;
                }
                else
                {
                    replyTo = briefUserInfoMap[stickers[i].ReplyTo!.UUID].Nickname;
                }
                dataList.Add(new StickerDataForClient(sticker: stickers[i], briefUserInfo: briefUserInfo, replyTo: replyTo, isLiked: isLikedDictionary[stickers[i].Id!].Result, likesNumber: (int)likesNumberDictionary[stickers[i].Id!].Result, repliesNumber: (int)repliesNumberDictionary[stickers[i].Id!].Result, trendValue: trendValues[i]));
            }

            return dataList;
        }
    }
}
