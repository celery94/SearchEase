using SearchEase.Server.Configuration;
using SearchEase.Server.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.Configure<IndexingConfiguration>(builder.Configuration.GetSection("Indexing"));
builder.Services.AddHostedService<LuceneIndexingService>();
builder.Services.AddScoped<SearchService>();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.UseDefaultFiles();
app.MapStaticAssets();

// Configure the HTTP request pipeline.

app.MapFallbackToFile("/index.html");

app.Run();
