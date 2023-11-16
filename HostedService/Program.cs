using HostedService.Redis;
using HostedService.TrendManager;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

//≈‰÷√Redis
builder.Services.AddSingleton<RedisConnection>();

//≈‰÷√HostedService
builder.Services.AddHostedService<TrendManagerHostService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

//app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
