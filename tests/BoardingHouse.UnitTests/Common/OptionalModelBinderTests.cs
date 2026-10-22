using BoardingHouse.Api.Common.ModelBinding;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Primitives;
using Moq;
using BoardingHouse.Application.Common;

namespace BoardingHouse.UnitTests.Common;

public class OptionalModelBinderTests
{
    private static ModelBindingContext NewContext<T>(Dictionary<string, StringValues> form)
    {
        var metadata = new EmptyModelMetadataProvider().GetMetadataForType(typeof(Optional<T>));
        return new DefaultModelBindingContext
        {
            ModelName = "note",
            ModelMetadata = metadata,
            ModelState = new ModelStateDictionary(),
            ValueProvider = new FormValueProvider(BindingSource.Form, new FormCollection(form), null),
            ActionContext = new Microsoft.AspNetCore.Mvc.ActionContext { HttpContext = new DefaultHttpContext() }
        };
    }

    [Fact]
    public async Task Bind_FieldMissing_LeavesUnset()
    {
        var context = NewContext<string?>([]);

        await new OptionalModelBinder<string?>().BindModelAsync(context);

        Assert.False(context.Result.IsModelSet);
    }

    [Fact]
    public async Task Bind_FieldEmpty_SetsNull()
    {
        var context = NewContext<string?>(new() { ["note"] = "" });

        await new OptionalModelBinder<string?>().BindModelAsync(context);

        var value = Assert.IsType<Optional<string?>>(context.Result.Model);
        Assert.True(value.IsSet);
        Assert.Null(value.Value);
    }

    [Fact]
    public async Task Bind_FieldWithValue_SetsValue()
    {
        var context = NewContext<string?>(new() { ["note"] = "Broken leg" });

        await new OptionalModelBinder<string?>().BindModelAsync(context);

        var value = Assert.IsType<Optional<string?>>(context.Result.Model);
        Assert.True(value.IsSet);
        Assert.Equal("Broken leg", value.Value);
    }

    [Fact]
    public async Task Bind_NullableValueType_ConvertsValue()
    {
        var context = NewContext<int?>(new() { ["note"] = "42" });

        await new OptionalModelBinder<int?>().BindModelAsync(context);

        Assert.Equal(42, Assert.IsType<Optional<int?>>(context.Result.Model).Value);
    }

    [Fact]
    public async Task Bind_InvalidValue_AddsModelError()
    {
        var context = NewContext<int?>(new() { ["note"] = "abc" });

        await new OptionalModelBinder<int?>().BindModelAsync(context);

        Assert.False(context.Result.IsModelSet);
        Assert.False(context.ModelState.IsValid);
    }

    [Fact]
    public async Task Bind_NonNullableValueTypeEmpty_AddsModelErrorInsteadOfDefault()
    {
        var context = NewContext<int>(new() { ["note"] = "" });

        await new OptionalModelBinder<int>().BindModelAsync(context);

        Assert.False(context.Result.IsModelSet);
        Assert.False(context.ModelState.IsValid);
    }

    [Fact]
    public async Task Bind_NullableValueTypeEmpty_SetsNull()
    {
        var context = NewContext<int?>(new() { ["note"] = "" });

        await new OptionalModelBinder<int?>().BindModelAsync(context);

        var value = Assert.IsType<Optional<int?>>(context.Result.Model);
        Assert.True(value.IsSet);
        Assert.Null(value.Value);
    }

    private static IModelBinder? BinderFor(Type type, BindingSource? source = null)
    {
        var context = new Mock<ModelBinderProviderContext>();
        context.SetupGet(c => c.Metadata).Returns(new EmptyModelMetadataProvider().GetMetadataForType(type));
        context.SetupGet(c => c.BindingInfo).Returns(new BindingInfo { BindingSource = source });
        return new OptionalModelBinderProvider().GetBinder(context.Object);
    }

    [Fact]
    public void Provider_ReturnsBinderOnlyForOptional()
    {
        Assert.IsType<OptionalModelBinder<string?>>(BinderFor(typeof(Optional<string?>)));
        Assert.Null(BinderFor(typeof(string)));
    }

    [Fact]
    public void Provider_ValueProviderSources_ReturnsBinder()
    {
        Assert.NotNull(BinderFor(typeof(Optional<string?>), BindingSource.Form));
        Assert.NotNull(BinderFor(typeof(Optional<string?>), BindingSource.Query));
        Assert.NotNull(BinderFor(typeof(Optional<string?>), BindingSource.Path));
    }

    [Fact]
    public void Provider_BodyOrHeaderSource_LeavesToTheirOwnBinders()
    {
        Assert.Null(BinderFor(typeof(Optional<string?>), BindingSource.Body));
        Assert.Null(BinderFor(typeof(Optional<string?>), BindingSource.Header));
    }

    [Fact]
    public void Provider_InnerTypeNotConvertibleFromString_LeavesToMvcBinders()
    {
        Assert.Null(BinderFor(typeof(Optional<List<Guid>?>), BindingSource.Form));
        Assert.Null(BinderFor(typeof(Optional<object?>), BindingSource.Form));
    }

    [Fact]
    public void Metadata_OnlyStringConvertibleOptionalsAreSimpleTypes()
    {
        var metadata = new EmptyModelMetadataProvider();

        Assert.False(metadata.GetMetadataForType(typeof(Optional<string?>)).IsComplexType);
        Assert.False(metadata.GetMetadataForType(typeof(Optional<int?>)).IsComplexType);
        Assert.True(metadata.GetMetadataForType(typeof(Optional<List<Guid>?>)).IsComplexType);
    }
}
