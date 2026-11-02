//--------------------------------------------------------------------------------
// Configure builder
//--------------------------------------------------------------------------------
Directory.SetCurrentDirectory(AppContext.BaseDirectory);
var builder = WebApplication.CreateBuilder(args);

// Logging
builder.ConfigureLogging();

// Http
builder.ConfigureHttp();

// Components
builder.ConfigureComponents();

//--------------------------------------------------------------------------------
// Configure the HTTP request pipeline.
//--------------------------------------------------------------------------------
var app = builder.Build();

// Startup information
app.LogStartupInformation();

// Static files
app.UseDefaultFiles();
app.UseStaticFiles();

// WebSocket
app.UseWebSockets();

// End point
app.MapEndpoints();

// Run
await app.RunAsync();
