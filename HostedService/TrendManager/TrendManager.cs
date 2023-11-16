using HostedService.Redis;
using StackExchange.Redis;

namespace HostedService.TrendManager
{
    public class TrendManagerHostService : BackgroundService
    {
        //依赖注入
        private readonly RedisConnection _redisConnection;

        public TrendManagerHostService(RedisConnection redisConnection)
        {
            _redisConnection = redisConnection;
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

        // 在Redis中生成 TrendListXXXX-XX-XX-XX（当前周期）（初始） 和 TrendListXXXX-XX-XX-XX（下一周期）（预置）
        private async void InitTrendList()
        {
            DateTime now = DateTime.Now;
            DateTime next = now.AddHours(2);
            DateTime expired = now.AddDays(-7);

            IDatabase stickerRedis = _redisConnection.GetStickerDatabase();
            if (stickerRedis.KeyExists($"TrendList{GetCycleSuffix(now)}"))
            {
                await stickerRedis.SortedSetCombineAndStoreAsync(SetOperation.Union, $"TrendList{GetCycleSuffix(next)}", keys: new RedisKey[] { $"TrendList{GetCycleSuffix(now)}", $"TrendCycle{GetCycleSuffix(expired)}" }, weights: new double[] { 1, -1 }, aggregate: Aggregate.Sum);

                var batch = stickerRedis.CreateBatch();

                _ = batch.SortedSetRemoveRangeByScoreAsync($"TrendList{GetCycleSuffix(next)}", double.MinValue, 0);
                _ = batch.KeyExpireAsync($"TrendList{GetCycleSuffix(now)}", TimeSpan.FromHours(8));
                _ = batch.KeyExpireAsync($"TrendCycle{GetCycleSuffix(now)}", TimeSpan.FromDays(8));

                batch.Execute();
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                InitTrendList();

                DateTime now = DateTime.Now;
                int hours = 0;
                int minutes = 59 - now.Minute;
                int seconds = 60 - now.Second;
                if (now.Hour % 2 == 0)
                {
                    hours = 1;
                }
                await Task.Delay(new TimeSpan(hours, minutes, seconds + 10));
            }
        }
    }
}
