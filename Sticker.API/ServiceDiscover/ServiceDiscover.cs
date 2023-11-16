using Consul;

namespace Sticker.API.ServiceDiscover
{
    public class ServiceDiscover : IServiceDiscover
    {
        private readonly ConsulClient _client;
        private readonly ILogger<ServiceDiscover> _logger;
        private readonly IConfiguration _configuration;

        public ServiceDiscover(ILogger<ServiceDiscover> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
            _client = new(options => options.Address = new Uri(_configuration["ServiceDiscover:Address"]!));
        }

        public string GetService(string serviceName)
        {
            var result = _client.Health.Service(serviceName).Result;
            if (result.StatusCode != System.Net.HttpStatusCode.OK)
            {
                _logger.LogError("Error：在使用Consul获取服务信息时失败，无法与Consul通信，返回状态码为[ {StatusCode} ]", result.StatusCode);
                throw new ConsulRequestException("与Consul通信失败", result.StatusCode);
            }
            List<AgentService> services = result.Response.Select(s => s.Service).ToList();
            if (services == null || !services.Any())
            {
                _logger.LogError("Error：在使用Consul获取服务信息[ {serviceName} ]时失败，没有找到可用的服务", serviceName);
                throw new ArgumentNullException($"获取服务信息{serviceName}失败！", nameof(services));
            }
            AgentService service = services.ElementAt(new Random().Next(0, services.Count));
            return $"http://{service.Address}:{service.Port}";
        }
    }
}
