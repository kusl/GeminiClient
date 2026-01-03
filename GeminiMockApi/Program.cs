var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
// Add CORS to allow requests from anywhere during testing
builder.Services.AddCors(options => options.AddPolicy("AllowAll", policy => 
    policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

var app = builder.Build();

app.UseCors("AllowAll");
app.MapControllers();

Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine("=================================================");
Console.WriteLine("   ♊ GEMINI MOCK API RUNNING");
Console.WriteLine("   📡 Listening on: http://localhost:5000");
Console.WriteLine("=================================================");
Console.ResetColor();

app.Run("http://localhost:5000");
