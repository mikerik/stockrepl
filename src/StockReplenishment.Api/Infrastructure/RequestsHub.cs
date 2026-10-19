using Microsoft.AspNetCore.SignalR;

namespace StockReplenishment.Api.Infrastructure;

/// <summary>Server-to-client only: clients listen for request updates, they don't call any hub methods.</summary>
public sealed class RequestsHub : Hub;
