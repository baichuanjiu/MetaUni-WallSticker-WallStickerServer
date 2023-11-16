using StackExchange.Redis;
using Sticker.API.MongoDBServices.Sticker;
using Sticker.API.Protos.Feed;
using Sticker.API.Redis;

namespace Sticker.API.TrendManager
{
    public class GetTrendRankWithTrendValueResponseData
    {
        public GetTrendRankWithTrendValueResponseData(List<string> stickers, List<double> trendValues)
        {
            this.stickers = stickers;
            this.trendValues = trendValues;
        }

        public List<string> stickers;
        public List<double> trendValues;
    }

    public class TrendManager
    {
        //依赖注入
        private readonly RedisConnection _redisConnection;
        private readonly StickerService _stickerService;
        private readonly AddFeed.AddFeedClient _rpcFeedClient;

        public TrendManager(RedisConnection redisConnection, StickerService stickerService, AddFeed.AddFeedClient rpcFeedClient)
        {
            _redisConnection = redisConnection;
            _stickerService = stickerService;
            _rpcFeedClient = rpcFeedClient;
        }



        // 传入DateTime，根据DateTime计算出对应的周期后缀
        // 计算规则为
        // ①：按两小时作为周期，从 当日 00:00 PM 起至 当日 23:59 PM 结束，一天共划分12个周期。
        // ②：XXXX年XX月XX日00:00PM - 01:59PM 后缀计算为：XXXX-XX-XX-01
        // ③：XXXX年XX月XX日02:00PM - 03:59PM 后缀计算为：XXXX-XX-XX-02
        // ④：依此类推，XXXX年XX月XX日22:00PM - 23:59PM 后缀计算为：XXXX-XX-XX-12
        private string GetCycleSuffix(DateTime dateTime)
        {
            return $"{dateTime.Year}-{dateTime.Month}-{dateTime.Day}-{(dateTime.Hour / 2) + 1}";
        }

        // 获取某条贴贴的热度值
        public async Task<double> GetTrendValueById(string id)
        {
            DateTime now = DateTime.Now;

            IDatabase stickerRedis = _redisConnection.GetStickerDatabase();
            double? trendValue = await stickerRedis.SortedSetScoreAsync($"TrendList{GetCycleSuffix(now)}", id);
            return trendValue ?? 0;
        }

        // 获取一组贴贴的热度值
        public async Task<List<double>> GetTrendValues(List<string> idList)
        {
            DateTime now = DateTime.Now;

            IDatabase stickerRedis = _redisConnection.GetStickerDatabase();
            List<RedisValue> queryList = new();
            idList.ForEach((id) =>
                {
                    queryList.Add(new RedisValue(id));
                });

            double?[] queryResults = await stickerRedis.SortedSetScoresAsync($"TrendList{GetCycleSuffix(now)}", queryList.ToArray());

            List<double> results = new();
            foreach (double? trendValue in queryResults)
            {
                results.Add(trendValue ?? 0);
            };

            return results;

        }

        // 获取当前热度排行榜中的一段数据（包含热度值）
        public async Task<GetTrendRankWithTrendValueResponseData> GetTrendRankWithTrendValueByRange(int start, int stop)
        {
            DateTime now = DateTime.Now;

            IDatabase stickerRedis = _redisConnection.GetStickerDatabase();
            SortedSetEntry[] sortedSetEntries = await stickerRedis.SortedSetRangeByRankWithScoresAsync($"TrendList{GetCycleSuffix(now)}", start, stop, order: Order.Descending);

            List<string> stickers = new();
            List<double> trendValues = new();
            sortedSetEntries.ToList().ForEach((value) =>
            {
                stickers.Add(value.Element!);
                trendValues.Add(value.Score);
            });
            return new(stickers, trendValues);
        }

        // 当某条贴贴被查看时，触发此热度机制，热度+2
        public async Task ReadAction(string id, int uuid)
        {
            IDatabase stickerRedis = _redisConnection.GetStickerDatabase();

            // 十分钟内重复查看不加热度
            if (await stickerRedis.KeyExistsAsync($"{uuid}reads{id}"))
            {
                return;
            }
            _ = stickerRedis.StringSetAsync($"{uuid}reads{id}", "", TimeSpan.FromMinutes(10));

            var batch = stickerRedis.CreateBatch();
            _ = TrendAction(batch, id, 2.0);
        }

        // 当某条贴贴被点赞时，触发此热度机制，热度+5
        public async Task LikeAction(string id, int uuid)
        {
            IDatabase stickerRedis = _redisConnection.GetStickerDatabase();

            // 一小时内重复点赞不加热度
            if (await stickerRedis.KeyExistsAsync($"{uuid}likes{id}"))
            {
                return;
            }
            _ = stickerRedis.StringSetAsync($"{uuid}likes{id}", "", TimeSpan.FromHours(1));

            var batch = stickerRedis.CreateBatch();
            _ = TrendAction(batch, id, 5.0);
        }

        // 当某条贴贴被回复时，触发此热度机制，热度+25
        public async Task ReplyAction(string id, int uuid)
        {
            IDatabase stickerRedis = _redisConnection.GetStickerDatabase();

            // 五分钟内重复回复不加热度
            if (await stickerRedis.KeyExistsAsync($"{uuid}replies{id}"))
            {
                return;
            }
            _ = stickerRedis.StringSetAsync($"{uuid}replies{id}", "", TimeSpan.FromMinutes(5));

            var batch = stickerRedis.CreateBatch();
            _ = TrendAction(batch,id,25.0);
        }

        private async Task TrendAction(IBatch batch,string id,double trendValue) 
        {
            DateTime now = DateTime.Now;
            DateTime next = now.AddHours(2);

            var result = batch.SortedSetIncrementAsync($"TrendList{GetCycleSuffix(now)}", id, trendValue);
            _ = batch.SortedSetIncrementAsync($"TrendCycle{GetCycleSuffix(now)}", id, trendValue);
            _ = batch.SortedSetIncrementAsync($"TrendList{GetCycleSuffix(next)}", id, trendValue);

            batch.Execute();

            batch.Wait(result);
            if (result.Result >= 500) 
            {
                IDatabase feedRedis = _redisConnection.GetFeedDatabase();
                // 七天内不进行重复推流
                if (await feedRedis.KeyExistsAsync($"{id}"))
                {
                    return;
                }
                _ = feedRedis.StringSetAsync($"{id}", "", TimeSpan.FromDays(7));

                //发送RPC推流请求
                var sticker = await _stickerService.GetStickerByIdAsync(id);
                var cover = sticker!.Medias.FirstOrDefault();

                AddFeedSingleRequest request;
                string title = "来墙贴";
                string description = "贴出你的想法";
                string openPageUrl = $"/miniApps/wallSticker/stickerDetailsPage/{sticker.Id}";
                if (cover != null)
                {
                    if (cover.Type == "video")
                    {
                        request = new()
                        {
                            Cover = new()
                            {
                                Type = cover.Type,
                                URL = cover.URL,
                                AspectRatio = cover.AspectRatio,
                                PreviewImage = cover.PreviewImage,
                                TimeTotal = cover.TimeTotal??0,
                            },
                            PreviewContent = sticker!.Text,
                            Title = title,
                            Description = description,
                            OpenPageUrl = openPageUrl,
                        };
                    }
                    else 
                    {
                        request = new()
                        {
                            Cover = new()
                            {
                                Type = cover.Type,
                                URL = cover.URL,
                                AspectRatio = cover.AspectRatio,
                            },
                            PreviewContent = sticker!.Text,
                            Title = title,
                            Description = description,
                            OpenPageUrl = openPageUrl,
                        };
                    }
                }
                else 
                {
                    request = new()
                    {
                        PreviewContent = sticker!.Text,
                        Title = title,
                        Description = description,
                        OpenPageUrl = openPageUrl,
                    };
                }

                _ = _rpcFeedClient.AddFeedSingleAsync(
                              request);
            }
        }
    }
}
