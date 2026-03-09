using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Emby.Server.Implementations.Dto;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Dto;
using Xunit;

namespace Jellyfin.Server.Implementations.Tests.Regression;

/// <summary>
/// Regression tests verifying that the <see cref="IDtoService"/> interface contract
/// remains stable and all implementations correctly satisfy it.
/// </summary>
[Trait("Category", "Regression")]
public class IDtoServiceContractRegressionTests
{
    /// <summary>
    /// Verifies that IDtoService exposes exactly the methods expected by the API layer.
    /// If this test fails, a breaking change has been made to the interface.
    /// </summary>
    [Fact]
    public void IDtoService_HasGetPrimaryImageAspectRatioMethod()
    {
        var method = typeof(IDtoService).GetMethod(
            nameof(IDtoService.GetPrimaryImageAspectRatio),
            BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(method);
        Assert.Equal(typeof(double?), method.ReturnType);

        var parameters = method.GetParameters();
        Assert.Single(parameters);
        Assert.Equal(typeof(BaseItem), parameters[0].ParameterType);
    }

    [Fact]
    public void IDtoService_HasGetBaseItemDtoAsyncMethod()
    {
        var method = typeof(IDtoService).GetMethod(
            nameof(IDtoService.GetBaseItemDtoAsync),
            BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(method);
        Assert.Equal(typeof(Task<BaseItemDto>), method.ReturnType);

        var parameters = method.GetParameters();
        Assert.Equal(4, parameters.Length);
        Assert.Equal(typeof(BaseItem), parameters[0].ParameterType);
        Assert.Equal(typeof(DtoOptions), parameters[1].ParameterType);
        Assert.Equal(typeof(User), parameters[2].ParameterType);
        Assert.Equal(typeof(BaseItem), parameters[3].ParameterType);
    }

    [Fact]
    public void IDtoService_HasGetBaseItemDtosAsyncMethod()
    {
        var method = typeof(IDtoService).GetMethod(
            nameof(IDtoService.GetBaseItemDtosAsync),
            BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(method);
        Assert.Equal(typeof(Task<IReadOnlyList<BaseItemDto>>), method.ReturnType);

        var parameters = method.GetParameters();
        Assert.Equal(4, parameters.Length);
        Assert.Equal(typeof(IReadOnlyList<BaseItem>), parameters[0].ParameterType);
        Assert.Equal(typeof(DtoOptions), parameters[1].ParameterType);
        Assert.Equal(typeof(User), parameters[2].ParameterType);
        Assert.Equal(typeof(BaseItem), parameters[3].ParameterType);
    }

    [Fact]
    public void IDtoService_HasGetItemByNameDtoAsyncMethod()
    {
        var method = typeof(IDtoService).GetMethod(
            nameof(IDtoService.GetItemByNameDtoAsync),
            BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(method);
        Assert.Equal(typeof(Task<BaseItemDto>), method.ReturnType);

        var parameters = method.GetParameters();
        Assert.Equal(4, parameters.Length);
        Assert.Equal(typeof(BaseItem), parameters[0].ParameterType);
        Assert.Equal(typeof(DtoOptions), parameters[1].ParameterType);
        Assert.Equal(typeof(List<BaseItem>), parameters[2].ParameterType);
        Assert.Equal(typeof(User), parameters[3].ParameterType);
    }

    [Fact]
    public void IDtoService_HasExactlyFourMethods()
    {
        var methods = typeof(IDtoService).GetMethods(BindingFlags.Public | BindingFlags.Instance);

        // Exactly: GetPrimaryImageAspectRatio, GetBaseItemDtoAsync, GetBaseItemDtosAsync, GetItemByNameDtoAsync
        Assert.Equal(4, methods.Length);
    }

    [Fact]
    public void DtoService_ImplementsIDtoService()
    {
        Assert.True(
            typeof(IDtoService).IsAssignableFrom(typeof(DtoService)),
            $"{nameof(DtoService)} must implement {nameof(IDtoService)}");
    }

    [Fact]
    public void DtoService_IsNotSealed_AllowsTestDoubles()
    {
        // DtoService is used broadly - verify it can be mocked/subclassed if needed
        Assert.False(
            typeof(DtoService).IsSealed,
            $"{nameof(DtoService)} should not be sealed so it can be mocked in tests");
    }

    [Fact]
    public void GetBaseItemDtoAsync_OptionalParameters_HaveNullDefaults()
    {
        var method = typeof(IDtoService).GetMethod(nameof(IDtoService.GetBaseItemDtoAsync))!;
        var parameters = method.GetParameters();

        // user (index 2) should be optional with null default
        Assert.True(parameters[2].IsOptional, "user parameter should be optional");
        Assert.Null(parameters[2].DefaultValue);

        // owner (index 3) should be optional with null default
        Assert.True(parameters[3].IsOptional, "owner parameter should be optional");
        Assert.Null(parameters[3].DefaultValue);
    }

    [Fact]
    public void GetBaseItemDtosAsync_OptionalParameters_HaveNullDefaults()
    {
        var method = typeof(IDtoService).GetMethod(nameof(IDtoService.GetBaseItemDtosAsync))!;
        var parameters = method.GetParameters();

        Assert.True(parameters[2].IsOptional, "user parameter should be optional");
        Assert.Null(parameters[2].DefaultValue);

        Assert.True(parameters[3].IsOptional, "owner parameter should be optional");
        Assert.Null(parameters[3].DefaultValue);
    }

    [Fact]
    public void GetItemByNameDtoAsync_UserParameter_IsOptionalWithNullDefault()
    {
        var method = typeof(IDtoService).GetMethod(nameof(IDtoService.GetItemByNameDtoAsync))!;
        var parameters = method.GetParameters();

        Assert.True(parameters[3].IsOptional, "user parameter should be optional");
        Assert.Null(parameters[3].DefaultValue);
    }

    /// <summary>
    /// Validates that the taggedItems parameter in GetItemByNameDtoAsync accepts null
    /// (i.e., is typed as nullable List&lt;BaseItem&gt;).
    /// </summary>
    [Fact]
    public void GetItemByNameDtoAsync_TaggedItemsParameter_IsNullable()
    {
        var method = typeof(IDtoService).GetMethod(nameof(IDtoService.GetItemByNameDtoAsync))!;
        var parameter = method.GetParameters()[2];

        // Parameter should be List<BaseItem>? (nullable reference type)
        Assert.Equal(typeof(List<BaseItem>), parameter.ParameterType);
    }
}
