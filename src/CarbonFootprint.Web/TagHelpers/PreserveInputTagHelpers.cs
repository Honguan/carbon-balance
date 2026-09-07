using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Primitives;

namespace CarbonFootprint.Web.TagHelpers;

[HtmlTargetElement("form", Attributes = "preserve-input-for")]
public sealed class PreserveInputFormTagHelper : TagHelper
{
    internal const string FormKey = "__preserveInputForm";
    private readonly FormValues _values = new();

    public string PreserveInputFor { get; set; } = string.Empty;
    public bool PreserveInputEditing { get; set; }

    [ViewContext, HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = null!;

    // Run before the framework form helper renders its children.
    public override int Order => -2000;

    public override void Init(TagHelperContext context) => context.Items[typeof(FormValues)] = _values;

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        output.Attributes.RemoveAll("preserve-input-for");
        output.Attributes.RemoveAll("preserve-input-editing");
        var request = ViewContext.HttpContext.Request;
        if (PreserveInputEditing && HttpMethods.IsGet(request.Method))
        {
            _values.Form = new FormCollection(ViewContext.ViewData.ModelState
                .Where(entry => entry.Value?.AttemptedValue is not null)
                .ToDictionary(entry => entry.Key, entry => new StringValues(entry.Value!.AttemptedValue), StringComparer.OrdinalIgnoreCase));
        }
        if (ViewContext.ViewData.ModelState.ErrorCount > 0 && HttpMethods.IsPost(request.Method) && request.HasFormContentType)
        {
            var form = await request.ReadFormAsync();
            var handler = context.AllAttributes["asp-page-handler"]?.Value?.ToString();
            if (form[FormKey] == PreserveInputFor &&
                string.Equals(request.Query["handler"], handler, StringComparison.OrdinalIgnoreCase))
            {
                _values.Form = form;
                output.Attributes.SetAttribute("data-unsaved", "true");
            }
        }

        var marker = new TagBuilder("input");
        marker.TagRenderMode = TagRenderMode.SelfClosing;
        marker.Attributes["type"] = "hidden";
        marker.Attributes["name"] = FormKey;
        marker.Attributes["value"] = PreserveInputFor;
        output.PostContent.AppendHtml(marker);
    }

    internal sealed class FormValues
    {
        public IFormCollection? Form { get; set; }
    }
}

[HtmlTargetElement("input", Attributes = "name")]
[HtmlTargetElement("textarea", Attributes = "name")]
[HtmlTargetElement("select", Attributes = "name")]
public sealed class PreserveInputControlTagHelper : TagHelper
{
    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        if (!context.Items.TryGetValue(typeof(PreserveInputFormTagHelper.FormValues), out var scope) ||
            scope is not PreserveInputFormTagHelper.FormValues { Form: { } form })
        {
            return;
        }

        var name = output.Attributes["name"]?.Value?.ToString() ?? string.Empty;
        if (name == PreserveInputFormTagHelper.FormKey || name == "__RequestVerificationToken")
        {
            return;
        }

        var values = form[name];
        if (output.TagName == "select")
        {
            context.Items[typeof(SelectedValues)] = new SelectedValues(values);
            output.Content.SetHtmlContent(await output.GetChildContentAsync());
        }
        else if (output.TagName == "textarea")
        {
            if (form.ContainsKey(name)) output.Content.SetContent(values.ToString());
        }
        else
        {
            var type = output.Attributes["type"]?.Value?.ToString()?.ToLowerInvariant();
            if (type is "hidden" or "password" or "file" or "submit" or "button" or "reset" or "image") return;

            if (type is "checkbox" or "radio")
            {
                var value = output.Attributes["value"]?.Value?.ToString() ?? "on";
                output.Attributes.RemoveAll("checked");
                if (values.Contains(value, StringComparer.Ordinal)) output.Attributes.SetAttribute("checked", "checked");
            }
            else if (form.ContainsKey(name))
            {
                output.Attributes.SetAttribute("value", values.ToString());
            }
        }
    }

    internal sealed record SelectedValues(StringValues Values);
}

[HtmlTargetElement("option")]
public sealed class PreserveInputOptionTagHelper : TagHelper
{
    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        if (!context.Items.TryGetValue(typeof(PreserveInputControlTagHelper.SelectedValues), out var scope) ||
            scope is not PreserveInputControlTagHelper.SelectedValues selected) return;

        var value = output.Attributes["value"]?.Value?.ToString() ??
            System.Net.WebUtility.HtmlDecode((await output.GetChildContentAsync()).GetContent());
        output.Attributes.RemoveAll("selected");
        if (selected.Values.Contains(value, StringComparer.Ordinal)) output.Attributes.SetAttribute("selected", "selected");
    }
}
