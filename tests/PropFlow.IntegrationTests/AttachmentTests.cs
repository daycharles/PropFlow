using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;
using Xunit;

namespace PropFlow.IntegrationTests;

[Collection("PostgreSQL")]
public sealed class AttachmentTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Admin_can_upload_list_download_and_delete_a_tenant_attachment()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync();
        using var form = new MultipartFormDataContent();
        var bytes = new ByteArrayContent("photo-bytes"u8.ToArray());
        bytes.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(bytes, "file", "repair-photo.png");
        form.Add(new StringContent("true"), "residentVisible");
        form.Add(new StringContent("2030-01-01T00:00:00Z"), "retainUntil");

        using var upload = await s.Client.PostAsync($"/api/work/{s.WorkA}/attachments/", form);
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        using var uploaded = JsonDocument.Parse(await upload.Content.ReadAsStringAsync());
        var id = uploaded.RootElement.GetProperty("id").GetGuid();
        Assert.True(uploaded.RootElement.GetProperty("residentVisible").GetBoolean());

        var list = await s.Client.GetFromJsonAsync<JsonElement>($"/api/work/{s.WorkA}/attachments/");
        Assert.Contains(list.EnumerateArray(), item => item.GetProperty("id").GetGuid() == id);

        using var download = await s.Client.GetAsync($"/api/work/{s.WorkA}/attachments/{id}");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("image/png", download.Content.Headers.ContentType?.MediaType);
        Assert.Equal("photo-bytes", await download.Content.ReadAsStringAsync());

        using var deleted = await s.Client.DeleteAsync($"/api/work/{s.WorkA}/attachments/{id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using var missing = await s.Client.GetAsync($"/api/work/{s.WorkA}/attachments/{id}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Read_only_users_cannot_upload_attachments()
    {
        await using var s = await fixture.CreateScenarioAsync();
        await s.LoginAsync(reader: true);
        using var form = new MultipartFormDataContent();
        var bytes = new ByteArrayContent("photo"u8.ToArray());
        bytes.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(bytes, "file", "photo.png");

        using var response = await s.Client.PostAsync($"/api/work/{s.WorkA}/attachments/", form);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
