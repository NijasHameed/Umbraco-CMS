using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Web.Models.ContentEditing;
using Umbraco.Core;
using Umbraco.Core.Mapping;
using Umbraco.Core.Models;
using Umbraco.Core.Models.PublishedContent;
using Umbraco.Core.Templates;
using Umbraco.Core.Services;
using Umbraco.Core.Strings;
using Umbraco.Web.Common.ActionsResults;
using Umbraco.Web.Common.Attributes;
using Umbraco.Web.Routing;

namespace Umbraco.Web.BackOffice.Controllers
{
    /// <summary>
    /// API controller to deal with Macro data
    /// </summary>
    [PluginController(Constants.Web.Mvc.BackOfficeApiArea)]
    public class MacroRenderingController : UmbracoAuthorizedJsonController
    {
        private readonly IMacroService _macroService;
        private readonly IUmbracoContextAccessor _umbracoContextAccessor;
        private readonly IShortStringHelper _shortStringHelper;
        private readonly ISiteDomainHelper _siteDomainHelper;
        private readonly UmbracoMapper _umbracoMapper;
        private readonly IUmbracoComponentRenderer _componentRenderer;
        private readonly IVariationContextAccessor _variationContextAccessor;


        public MacroRenderingController(
            UmbracoMapper umbracoMapper,
            IUmbracoComponentRenderer componentRenderer,
            IVariationContextAccessor variationContextAccessor,
            IMacroService macroService,
            IUmbracoContextAccessor umbracoContextAccessor,
            IShortStringHelper shortStringHelper,
            ISiteDomainHelper siteDomainHelper)

        {
            _umbracoMapper = umbracoMapper ?? throw new ArgumentNullException(nameof(umbracoMapper));
            _componentRenderer = componentRenderer ?? throw new ArgumentNullException(nameof(componentRenderer));
            _variationContextAccessor = variationContextAccessor ?? throw new ArgumentNullException(nameof(variationContextAccessor));
            _macroService = macroService ?? throw new ArgumentNullException(nameof(macroService));
            _umbracoContextAccessor = umbracoContextAccessor ?? throw new ArgumentNullException(nameof(umbracoContextAccessor));
            _shortStringHelper = shortStringHelper ?? throw new ArgumentNullException(nameof(shortStringHelper));
            _siteDomainHelper = siteDomainHelper ?? throw new ArgumentNullException(nameof(siteDomainHelper));
        }

        /// <summary>
        /// Gets the macro parameters to be filled in for a particular macro
        /// </summary>
        /// <returns></returns>
        /// <remarks>
        /// Note that ALL logged in users have access to this method because editors will need to insert macros into rte (content/media/members) and it's used for
        /// inserting into templates/views/etc... it doesn't expose any sensitive data.
        /// </remarks>
        public ActionResult<IEnumerable<MacroParameter>> GetMacroParameters(int macroId)
        {
            var macro = _macroService.GetById(macroId);
            if (macro == null)
            {
                return new ValidationErrorResult(macro, StatusCodes.Status404NotFound);
            }

            return new ActionResult<IEnumerable<MacroParameter>>(_umbracoMapper.Map<IEnumerable<MacroParameter>>(macro).OrderBy(x => x.SortOrder));
        }

        /// <summary>
        /// Gets a rendered macro as HTML for rendering in the rich text editor
        /// </summary>
        /// <param name="macroAlias"></param>
        /// <param name="pageId"></param>
        /// <param name="macroParams">
        /// To send a dictionary as a GET parameter the query should be structured like:
        ///
        /// ?macroAlias=Test&pageId=3634&macroParams[0].key=myKey&macroParams[0].value=myVal&macroParams[1].key=anotherKey&macroParams[1].value=anotherVal
        ///
        /// </param>
        /// <returns></returns>
        [HttpGet]
        public IActionResult GetMacroResultAsHtmlForEditor(string macroAlias, int pageId, [FromQuery] IDictionary<string, object> macroParams)
        {
            return GetMacroResultAsHtml(macroAlias, pageId, macroParams);
        }

        /// <summary>
        /// Gets a rendered macro as HTML for rendering in the rich text editor.
        /// Using HTTP POST instead of GET allows for more parameters to be passed as it's not dependent on URL-length limitations like GET.
        /// The method using GET is kept to maintain backwards compatibility
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        [HttpPost]
        public IActionResult GetMacroResultAsHtmlForEditor(MacroParameterModel model)
        {
            return GetMacroResultAsHtml(model.MacroAlias, model.PageId, model.MacroParams);
        }

        public class MacroParameterModel
        {
            public string MacroAlias { get; set; }
            public int PageId { get; set; }
            public IDictionary<string, object> MacroParams { get; set; }
        }

        private IActionResult GetMacroResultAsHtml(string macroAlias, int pageId, IDictionary<string, object> macroParams)
        {
            var m = _macroService.GetByAlias(macroAlias);
            if (m == null)
                return new ValidationErrorResult(m, StatusCodes.Status404NotFound);

            var umbracoContext = _umbracoContextAccessor.GetRequiredUmbracoContext();
            var publishedContent = umbracoContext.Content.GetById(true, pageId);

            //if it isn't supposed to be rendered in the editor then return an empty string
            //currently we cannot render a macro if the page doesn't yet exist
            if (pageId == -1 || publishedContent == null || m.DontRender)
            {
                //need to create a specific content result formatted as HTML since this controller has been configured
                //with only json formatters.
                return Content(string.Empty, "text/html", Encoding.UTF8);
            }


            // When rendering the macro in the backoffice the default setting would be to use the Culture of the logged in user.
            // Since a Macro might contain thing thats related to the culture of the "IPublishedContent" (ie Dictionary keys) we want
            // to set the current culture to the culture related to the content item. This is hacky but it works.

            // fixme
            // in a 1:1 situation we do not handle the language being edited
            // so the macro renders in the wrong language

            var culture = DomainUtilities.GetCultureFromDomains(publishedContent.Id, publishedContent.Path, null, umbracoContext, _siteDomainHelper);

            if (culture != null)
                Thread.CurrentThread.CurrentCulture = Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo(culture);

            // must have an active variation context!
            _variationContextAccessor.VariationContext = new VariationContext(culture);

            using (umbracoContext.ForcedPreview(true))
            {
                //need to create a specific content result formatted as HTML since this controller has been configured
                //with only json formatters.
                return Content(_componentRenderer.RenderMacroForContent(publishedContent, m.Alias, macroParams).ToString(), "text/html",
                    Encoding.UTF8);
            }
        }

        [HttpPost]
        public IActionResult CreatePartialViewMacroWithFile(CreatePartialViewMacroWithFileModel model)
        {
            if (model == null) throw new ArgumentNullException("model");
            if (string.IsNullOrWhiteSpace(model.Filename)) throw new ArgumentException("Filename cannot be null or whitespace", "model.Filename");
            if (string.IsNullOrWhiteSpace(model.VirtualPath)) throw new ArgumentException("VirtualPath cannot be null or whitespace", "model.VirtualPath");

            var macroName = model.Filename.TrimEnd(".cshtml");

            var macro = new Macro(_shortStringHelper)
            {
                Alias = macroName.ToSafeAlias(_shortStringHelper),
                Name = macroName,
                MacroSource = model.VirtualPath.EnsureStartsWith("~")
            };

            _macroService.Save(macro); // may throw
            return Ok();
        }

        public class CreatePartialViewMacroWithFileModel
        {
            public string Filename { get; set; }
            public string VirtualPath { get; set; }
        }
    }
}
