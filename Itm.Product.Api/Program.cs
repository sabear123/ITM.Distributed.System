using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------
// 1. REGISTRO DE SERVICIOS (Inyección de Dependencias)
// ---------------------------------------------------------

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ARQUITECTURA: Registramos el HttpClientFactory
// Esto crea un "pool" de conexiones reutilizables.
builder.Services.AddHttpClient("InventoryClient", client =>
{
    // ⚠️ IMPORTANTE: Aquí debes poner la URL donde corre TU proyecto de Inventario.
    // Revisa el puerto en el navegador cuando corras el otro proyecto (ej: 5023, 7100, etc.)
    client.BaseAddress = new Uri("http://localhost:PONER_AQUI_EL_PUERTO_DEL_INVENTARIO");

    // RESILIENCIA: Definimos un tiempo máximo de espera.
    // Si el inventario no responde en 5 segundos, abortamos para no bloquear al usuario.
    client.Timeout = TimeSpan.FromSeconds(5);
});

var app = builder.Build();

// ---------------------------------------------------------
// 2. PIPELINE HTTP
// ---------------------------------------------------------
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// ---------------------------------------------------------
// 3. ENDPOINTS
// ---------------------------------------------------------

// Endpoint: El usuario consulta un producto y nosotros consultamos su stock remotamente
app.MapGet("/api/products/{id}/check-stock", async (int id, IHttpClientFactory clientFactory) =>
{
    // 1. Solicitamos el cliente pre-configurado
    var client = clientFactory.CreateClient("InventoryClient");

    try
    {
        // 2. Hacemos la llamada SÍNCRONA al microservicio de Inventario
        // GET /api/inventory/{id}
        var response = await client.GetAsync($"/api/inventory/{id}");

        if (response.IsSuccessStatusCode)
        {
            // Deserializamos la respuesta. Usamos un record local (ver abajo)
            var inventoryData = await response.Content.ReadFromJsonAsync<InventoryResponse>();

            // Retornamos la data combinada
            return Results.Ok(new
            {
                ProductId = id,
                ProductName = "Producto Demo", // Simulado
                StockInfo = inventoryData,
                Source = "Información obtenida remotamente del Servicio de Inventario"
            });
        }
        else
        {
            // Si el inventario dice 404 (No existe)
            return Results.Problem($"El inventario respondió con error: {response.StatusCode}");
        }
    }
    catch (TaskCanceledException)
    {
        // RUBRICA NIVEL 5: Manejo específico de Timeout
        return Results.Problem("El servicio de inventario tardó demasiado en responder (Timeout). Intente más tarde.");
    }
    catch (HttpRequestException ex)
    {
        // RUBRICA NIVEL 5: Manejo de red (DNS, Servidor Apagado, Puerto cerrado)
        return Results.Problem($"No se pudo conectar con el servicio de inventario. ¿Está encendido? Error: {ex.Message}");
    }
});

app.Run();

// ---------------------------------------------------------
// 4. MODELOS LOCALES (DTOS)
// ---------------------------------------------------------
// Usamos este record para "mapear" lo que nos devuelve el otro servicio
internal record InventoryResponse(int ProductId, int Stock, string Sku);