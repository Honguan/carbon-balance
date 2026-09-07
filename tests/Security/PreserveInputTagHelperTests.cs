using System.Text.Encodings.Web;
using CarbonFootprint.Web.TagHelpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Primitives;

namespace CarbonFootprint.Security.Tests;

public sealed class PreserveInputTagHelperTests
{
    [Fact]
    public async Task FailedPost_RestoresOnlySubmittedFormAndEncodesInput()
    {
        var posted = new Dictionary<string, StringValues>
        {
            ["__preserveInputForm"] = "ReviewInventory-row-1",
            ["reviewComment"] = "<script>alert(1)</script>\"&"
        };
        var submitted = await FormAsync("ReviewInventory-row-1", posted);
        var otherRow = await FormAsync("ReviewInventory-row-2", posted);
        var otherHandler = await FormAsync("ReviewInventory-row-1", posted, handler: "CreateProduct");
        var input = await ControlAsync(submitted, "input", "reviewComment", ("value", "original"));
        var textarea = await ControlAsync(submitted, "textarea", "reviewComment");

        Assert.Equal(posted["reviewComment"].ToString(), input.Attributes["value"].Value);
        Assert.Contains("&lt;script&gt;", Render(input));
        Assert.Contains("&quot;&amp;", Render(input));
        Assert.Contains("&lt;script&gt;", Render(textarea));
        Assert.DoesNotContain("<script>", Render(textarea));
        foreach (var scope in new[] { otherRow, otherHandler })
        {
            var untouched = await ControlAsync(scope, "input", "reviewComment", ("value", "original"));
            Assert.Equal("original", untouched.Attributes["value"].Value);
        }
    }

    [Theory]
    [InlineData("GET", false)]
    [InlineData("POST", true)]
    public async Task InitialGetOrSuccessfulPost_DoesNotRestore(string method, bool valid)
    {
        var scope = await FormAsync("form", new()
        {
            ["__preserveInputForm"] = "form",
            ["name"] = "submitted"
        }, method: method, valid: valid);
        var output = await ControlAsync(scope, "input", "name", ("value", "original"));
        Assert.Equal("original", output.Attributes["value"].Value);
    }

    [Theory]
    [InlineData(false, "original")]
    [InlineData(true, "saved activity")]
    public async Task EditGet_PrefillsOnlyExplicitlyOptedInForm(bool editing, string expected)
    {
        var scope = await FormAsync("form", new() { ["name"] = "saved activity" },
            method: "GET", valid: true, editing: editing);
        var output = await ControlAsync(scope, "input", "name", ("value", "original"));
        Assert.Equal(expected, output.Attributes["value"].Value);
    }

    [Theory]
    [InlineData("hidden")]
    [InlineData("password")]
    [InlineData("file")]
    public async Task ProtectedControls_KeepServerValue(string type)
    {
        var scope = await FormAsync("form", new()
        {
            ["__preserveInputForm"] = "form",
            ["secret"] = "submitted"
        });
        var output = await ControlAsync(scope, "input", "secret", ("type", type), ("value", "server"));
        Assert.Equal("server", output.Attributes["value"].Value);
    }

    [Fact]
    public async Task FailedPost_RestoresUncheckedCheckboxAndSelectOptions()
    {
        var scope = await FormAsync("form", new()
        {
            ["__preserveInputForm"] = "form",
            ["unit"] = "kg",
            ["enabled"] = "true"
        });
        var uncheckedBox = await ControlAsync(scope, "input", "absent", ("type", "checkbox"), ("checked", "checked"));
        var checkedBox = await ControlAsync(scope, "input", "enabled", ("type", "checkbox"), ("value", "true"));
        Assert.False(uncheckedBox.Attributes.ContainsName("checked"));
        Assert.True(checkedBox.Attributes.ContainsName("checked"));

        var selectContext = Context("select", new(scope.Items), ("name", "unit"));
        var options = new List<TagHelperOutput>();
        var select = new TagHelperOutput("select", new TagHelperAttributeList(selectContext.AllAttributes), async (_, _) =>
        {
            foreach (var unit in new[] { "kg", "g" })
            {
                var optionContext = Context("option", new(selectContext.Items), ("value", unit), ("selected", "selected"));
                var option = Output("option", optionContext.AllAttributes);
                await new PreserveInputOptionTagHelper().ProcessAsync(optionContext, option);
                options.Add(option);
            }
            return new DefaultTagHelperContent();
        });
        await new PreserveInputControlTagHelper().ProcessAsync(selectContext, select);
        Assert.True(options[0].Attributes.ContainsName("selected"));
        Assert.False(options[1].Attributes.ContainsName("selected"));
    }

    private static async Task<TagHelperContext> FormAsync(string key, Dictionary<string, StringValues> posted,
        string handler = "ReviewInventory", string method = "POST", bool valid = false, bool editing = false)
    {
        var http = new DefaultHttpContext();
        http.Request.Method = method;
        http.Request.ContentType = "application/x-www-form-urlencoded";
        http.Request.QueryString = new QueryString("?handler=ReviewInventory");
        http.Request.Form = new FormCollection(posted);
        var viewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary());
        foreach (var entry in posted) viewData.ModelState.SetModelValue(entry.Key, entry.Value, entry.Value.ToString());
        if (!valid) viewData.ModelState.AddModelError("", "Validation failed");
        var helper = new PreserveInputFormTagHelper
        {
            PreserveInputFor = key,
            PreserveInputEditing = editing,
            ViewContext = new ViewContext { HttpContext = http, ViewData = viewData }
        };
        var context = Context("form", new(), ("asp-page-handler", handler));
        helper.Init(context);
        var output = Output("form", context.AllAttributes);
        await helper.ProcessAsync(context, output);
        Assert.Contains("name=\"__preserveInputForm\"", Render(output));
        return context;
    }

    private static async Task<TagHelperOutput> ControlAsync(TagHelperContext parent, string tag, string name,
        params (string Name, string Value)[] attributes)
    {
        var context = Context(tag, new(parent.Items), attributes.Prepend(("name", name)).ToArray());
        var output = Output(tag, context.AllAttributes);
        await new PreserveInputControlTagHelper().ProcessAsync(context, output);
        return output;
    }

    private static TagHelperContext Context(string tag, Dictionary<object, object> items,
        params (string Name, string Value)[] attributes) =>
        new(tag, new TagHelperAttributeList(attributes.Select(a => new TagHelperAttribute(a.Name, a.Value))), items, Guid.NewGuid().ToString());

    private static TagHelperOutput Output(string tag, ReadOnlyTagHelperAttributeList attributes) =>
        new(tag, new TagHelperAttributeList(attributes), (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));

    private static string Render(TagHelperOutput output)
    {
        using var writer = new StringWriter();
        output.WriteTo(writer, HtmlEncoder.Default);
        return writer.ToString();
    }
}
