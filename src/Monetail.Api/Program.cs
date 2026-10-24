var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHealthChecks();

var app = builder.Build();
app.MapHealthChecks("/health");
app.Run();

/// <summary>統合テストからAPIの起動処理を参照する。</summary>
public partial class Program;
