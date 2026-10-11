
var builder = WebApplication.CreateBuilder(args); // Prepara el host de ASP.NET Core y configura los servicios necesarios para la aplicación web.
var app = builder.Build(); // CONSTRUYE la aplicación web a partir de la configuración y servicios definidos en el builder.

app.Run(); // iniciar la aplicación web y comenzar a escuchar las solicitudes entrantes.
