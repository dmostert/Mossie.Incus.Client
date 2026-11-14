using Mossie.Incus.Client.Api;

namespace Mossie.Incus.Client.Tests;

/// <summary>Tests for generated constants and error types.</summary>
public sealed class ConstantsAndErrorsTests
{
    [Fact]
    public void ResponseTypes_MatchGoValues()
    {
        Assert.Equal("sync", IncusResponseType.SyncResponse);
        Assert.Equal("async", IncusResponseType.AsyncResponse);
        Assert.Equal("error", IncusResponseType.ErrorResponse);
    }

    [Fact]
    public void StatusCodes_MatchGoValues()
    {
        Assert.Equal(100, IncusStatusCode.OperationCreated);
        Assert.Equal(103, IncusStatusCode.Running);
        Assert.Equal(200, IncusStatusCode.Success);
        Assert.Equal(400, IncusStatusCode.Failure);
    }

    [Fact]
    public void ApiConstants_MatchGoValues()
    {
        Assert.Equal("tls", IncusConstants.AuthenticationMethodTLS);
        Assert.Equal("oidc", IncusConstants.AuthenticationMethodOIDC);
        Assert.Equal("container", IncusConstants.InstanceTypeContainer);
        Assert.Equal("virtual-machine", IncusConstants.InstanceTypeVM);
    }

    [Fact]
    public void IncusStatusException_ExposesStatusCodeAndMessage()
    {
        IncusStatusException exception = new(404, "Image not found");

        Assert.Equal(404, exception.StatusCode);
        Assert.Equal("Image not found", exception.Message);
        Assert.True(exception.Matches(404));
        Assert.False(exception.Matches(400));
    }

    [Fact]
    public void IncusStatusException_PreservesInnerException()
    {
        InvalidOperationException inner = new("boom");

        IncusStatusException exception = new(500, "failure", inner);

        Assert.Same(inner, exception.InnerException);
        Assert.Equal(500, exception.StatusCode);
    }

    [Fact]
    public void InstanceTypeExtensions_MapToApiStrings()
    {
        Assert.Equal("container", InstanceType.Container.ToApiString());
        Assert.Equal("virtual-machine", InstanceType.VirtualMachine.ToApiString());
        Assert.Equal("", InstanceType.Any.ToApiString());
    }
}
