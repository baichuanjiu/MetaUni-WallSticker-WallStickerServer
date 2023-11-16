using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc;
using Sticker.API.ReusableClass;
using System.Text.Json;
using Sticker.API.Protos.Authentication;
using Grpc.Core;

namespace Sticker.API.Filters
{
    public class JWTAuthFilterService : IAsyncActionFilter
    {
        //依赖注入
        private readonly ILogger<JWTAuthFilterService> _logger;
        private readonly Authenticate.AuthenticateClient _rpcAuthClient;

        public JWTAuthFilterService(ILogger<JWTAuthFilterService> logger, Authenticate.AuthenticateClient rpcAuthClient)
        {
            _logger = logger;
            _rpcAuthClient = rpcAuthClient;
        }

        async Task IAsyncActionFilter.OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            //验证JWT
            try
            {
                AuthJWTRequest request = new()
                {
                    JWT = context.ActionArguments["JWT"] as string,
                    UUID = int.Parse(context.ActionArguments["UUID"]!.ToString()!)
                };

                AuthJWTReply reply = await _rpcAuthClient.AuthJWTAsync(
                              request);
                if (reply.IsValid)
                {
                    await next();
                }
                else
                {
                    _logger.LogWarning("Warning：用户[ {UUID} ]在访问[ {controller} ]时使用了无效的JWT。", context.ActionArguments["UUID"]!.ToString()!, context.Controller.ToString());
                    ResponseT<string> authorizationFailed = new(1, "使用了无效的JWT，请重新登录");
                    context.Result = new ContentResult
                    {
                        StatusCode = 200,
                        ContentType = "application/json",
                        Content = JsonSerializer.Serialize(authorizationFailed, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }),
                    };
                }
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.PermissionDenied)
            {
                _logger.LogError("Error：在调用RPC接口进行鉴权时出错，错误类型{StatusCode}，报错信息为{ex}。", StatusCode.PermissionDenied, ex);
                context.Result = new ContentResult
                {
                    StatusCode = 500,
                    ContentType = "application/json",
                };
            }
            catch (RpcException ex)
            {
                _logger.LogError("Error：在调用RPC接口进行鉴权时出错，报错信息为{ex}。", ex);
                context.Result = new ContentResult
                {
                    StatusCode = 500,
                    ContentType = "application/json",
                };
            }
        }
    }
}
