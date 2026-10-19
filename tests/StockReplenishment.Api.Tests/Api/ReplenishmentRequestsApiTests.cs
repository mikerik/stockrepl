using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using StockReplenishment.Api.Tests.Support;
using StockReplenishment.Contracts.Common;
using StockReplenishment.Contracts.Requests;

namespace StockReplenishment.Api.Tests.Api;

[TestFixture]
public class ReplenishmentRequestsApiTests
{
    private const string RequestsUrl = "/api/replenishment-requests";

    private ApiFactory _factory = null!;
    private HttpClient _worker = null!;
    private HttpClient _reviewer = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _factory = new ApiFactory();
        _worker = _factory.CreateClientAs(TestData.Worker);
        _reviewer = _factory.CreateClientAs(TestData.Reviewer);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _worker.Dispose();
        _reviewer.Dispose();
        _factory.Dispose();
    }

    [Test]
    public async Task List_ReturnsPagedSeedData()
    {
        var page = await _worker.GetFromJsonAsync<PagedResult<ReplenishmentRequestSummaryDto>>($"{RequestsUrl}?page=2&pageSize=5");

        Assert.That(page, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(page!.Page, Is.EqualTo(2));
            Assert.That(page.Items, Has.Count.EqualTo(5));
            Assert.That(page.TotalCount, Is.GreaterThanOrEqualTo(30));
        });
    }

    [TestCase("pageSize=0")]
    [TestCase("pageSize=101")]
    [TestCase("page=0")]
    [TestCase("status=Unknown")]
    public async Task List_InvalidQuery_Returns400(string query)
    {
        var response = await _worker.GetAsync($"{RequestsUrl}?{query}");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Get_UnknownId_Returns404ProblemDetails()
    {
        var response = await _worker.GetAsync($"{RequestsUrl}/999999");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/problem+json"));
    }

    [Test]
    public async Task UnknownApiRoute_Returns404InsteadOfClientPage()
    {
        var response = await _worker.GetAsync("/api/does-not-exist");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task List_PageBeyondLimit_Returns400InsteadOfOverflowing()
    {
        var response = await _worker.GetAsync($"{RequestsUrl}?page={int.MaxValue}&pageSize=100");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task KnownRouteWithWrongMethod_Returns405()
    {
        var response = await _worker.PatchAsync($"{RequestsUrl}/1", JsonContent.Create(new { }));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.MethodNotAllowed));
    }

    [TestCase("/")]
    [TestCase("/requests/5")]
    [TestCase("/requests/new")]
    public async Task ClientRoutes_ServeTheBlazorApp(string path)
    {
        var response = await _worker.GetAsync(path);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/html"));
    }

    [Test]
    public async Task SwaggerUi_IsServedInDevelopment()
    {
        var response = await _worker.GetAsync("/swagger/index.html");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("swagger-ui"));
    }

    [Test]
    public async Task OpenApiDocument_DocumentsTheUserHeaderOnOperations()
    {
        var document = await _worker.GetStringAsync("/openapi/v1.json");

        Assert.That(document, Does.Contain("\"Stock Replenishment API\""));
        Assert.That(document, Does.Contain(ApiConventions.UserHeader));
    }

    [Test]
    public async Task Create_WithNullLineItem_Returns400()
    {
        var response = await _worker.PostAsync(RequestsUrl, JsonContent.Create(new { locationId = 1, lines = new object?[] { null } }));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Fulfill_WithNullLineItem_Returns400()
    {
        var response = await _worker.PostAsync($"{RequestsUrl}/1/fulfill", JsonContent.Create(new { lines = new object?[] { null } }));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Create_WithoutLines_Returns400WithFieldError()
    {
        var response = await _worker.PostAsJsonAsync(RequestsUrl, new SaveRequestInput { LocationId = 1, Lines = [] });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.That(problem!.Errors.Keys, Does.Contain(nameof(SaveRequestInput.Lines)));
    }

    [Test]
    public async Task Create_Returns201WithLocationHeader()
    {
        var response = await _worker.PostAsJsonAsync(RequestsUrl, NewRequest());

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var created = await response.Content.ReadFromJsonAsync<ReplenishmentRequestDto>();
        Assert.That(response.Headers.Location?.AbsolutePath, Is.EqualTo($"{RequestsUrl}/{created!.Id}"));
        Assert.That(created.RequestedBy, Is.EqualTo(TestData.Worker));
    }

    [Test]
    public async Task Submit_Returns202AndStockCheckCompletesInBackground()
    {
        var created = await CreateAsync();

        var response = await _worker.PostAsync($"{RequestsUrl}/{created.Id}/submit", null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
        Assert.That(response.Headers.Location?.AbsolutePath, Is.EqualTo($"{RequestsUrl}/{created.Id}"));
        var checkedRequest = await WaitForStockCheckAsync(created.Id);
        Assert.That(checkedRequest.StockCheckStatus, Is.EqualTo(StockCheckStatus.Completed));
        Assert.That(checkedRequest.AllItemsAvailable, Is.True);
    }

    [Test]
    public async Task FullWorkflow_ApproveByReviewerAndFulfill()
    {
        var created = await CreateAsync();
        await _worker.PostAsync($"{RequestsUrl}/{created.Id}/submit", null);
        await WaitForStockCheckAsync(created.Id);

        var ownApproval = await _worker.PostAsync($"{RequestsUrl}/{created.Id}/approve", null);
        var approval = await _reviewer.PostAsJsonAsync($"{RequestsUrl}/{created.Id}/approve", new ApproveRequestInput { Comment = "OK" });
        var approved = await approval.Content.ReadFromJsonAsync<ReplenishmentRequestDto>();
        var fulfil = await _reviewer.PostAsJsonAsync($"{RequestsUrl}/{created.Id}/fulfill", new FulfillRequestInput
        {
            Lines = approved!.Lines.Select(l => new FulfilledLineInput { LineId = l.Id, FulfilledQuantity = l.RequestedQuantity }).ToList(),
        });

        Assert.Multiple(() =>
        {
            Assert.That(ownApproval.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
            Assert.That(approval.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(fulfil.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        });
    }

    [Test]
    public async Task Reject_WithoutReason_Returns400()
    {
        var created = await CreateAsync();
        await _worker.PostAsync($"{RequestsUrl}/{created.Id}/submit", null);

        var response = await _reviewer.PostAsJsonAsync($"{RequestsUrl}/{created.Id}/reject", new { reason = "" });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Delete_SubmittedRequest_Returns409()
    {
        var created = await CreateAsync();
        await _worker.PostAsync($"{RequestsUrl}/{created.Id}/submit", null);

        var response = await _worker.DeleteAsync($"{RequestsUrl}/{created.Id}");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    [Test]
    public async Task Delete_Draft_Returns204ThenGetReturns404()
    {
        var created = await CreateAsync();

        var delete = await _worker.DeleteAsync($"{RequestsUrl}/{created.Id}");
        var get = await _worker.GetAsync($"{RequestsUrl}/{created.Id}");

        Assert.That(delete.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That(get.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    private async Task<ReplenishmentRequestDto> CreateAsync()
    {
        var response = await _worker.PostAsJsonAsync(RequestsUrl, NewRequest());
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ReplenishmentRequestDto>())!;
    }

    /// <summary>Polls the resource the way a non-SignalR client would.</summary>
    private async Task<ReplenishmentRequestDto> WaitForStockCheckAsync(int id)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (true)
        {
            var request = (await _worker.GetFromJsonAsync<ReplenishmentRequestDto>($"{RequestsUrl}/{id}"))!;
            if (request.StockCheckStatus != StockCheckStatus.Pending || DateTime.UtcNow > deadline)
            {
                return request;
            }

            await Task.Delay(50);
        }
    }

    private static SaveRequestInput NewRequest() => new()
    {
        LocationId = 1,
        Priority = RequestPriority.Urgent,
        Lines = [new RequestLineInput { ArticleNumber = "100-2045", RequestedQuantity = 50 }],
    };
}
