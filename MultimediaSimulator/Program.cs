using MultimediaSimulator.Streaming;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.Configure<StreamingOptions>(builder.Configuration.GetSection(StreamingOptions.SectionName));
// short timeout: /start waits on this call, so a hung Wowza should fail fast (502) instead of after 100 s
builder.Services.AddHttpClient(FfmpegUavStreamService.WowzaHttpClientName, client => client.Timeout = TimeSpan.FromSeconds(5));
builder.Services.AddSingleton<IUavStreamService, FfmpegUavStreamService>();


var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthorization();

app.MapControllers();

app.Run();
