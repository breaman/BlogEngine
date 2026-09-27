using BlogEngine.Client.Services;

namespace BlogEngine.UnitTests.Client;

/// <summary>
/// Tests <see cref="MediaUploadResponse"/>, shared by the upload zone and the editor's paste and drop upload (T4.2).
/// </summary>
public class MediaUploadResponseTests
{
    /// <summary>A 200 carries the file's result; anything else, or an unreadable body, has none.</summary>
    [Test]
    public async Task ReadResult_OnlyFromReadable200()
    {
        const string body = """[{"fileName":"a.png","error":"Nope."}]""";

        await Assert.That(MediaUploadResponse.ReadResult(200, body)!.Error).IsEqualTo("Nope.");
        await Assert.That(MediaUploadResponse.ReadResult(500, body)).IsNull();
        await Assert.That(MediaUploadResponse.ReadResult(200, "<html>")).IsNull();
        await Assert.That(MediaUploadResponse.ReadResult(200, string.Empty)).IsNull();
    }

    /// <summary>Failures without a file result get a message for their status, or the problem document's detail.</summary>
    [Test]
    [Arguments(0, "", "The upload failed. Check your connection and try again.")]
    [Arguments(401, "", "Your session has expired. Sign in again and retry.")]
    [Arguments(413, "", "The file is too large to upload.")]
    [Arguments(400, """{"title":"Bad","detail":"No files were sent."}""", "No files were sent.")]
    [Arguments(500, "oops", "The upload failed (HTTP 500).")]
    [Arguments(500, "[1]", "The upload failed (HTTP 500).")]
    public async Task DescribeFailure_ExplainsStatus(int status, string body, string expected)
    {
        await Assert.That(MediaUploadResponse.DescribeFailure(status, body)).IsEqualTo(expected);
    }
}