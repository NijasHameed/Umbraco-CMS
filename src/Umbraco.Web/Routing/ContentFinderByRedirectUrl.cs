﻿using Umbraco.Core;
using Umbraco.Core.Logging;
using Umbraco.Core.Services;

namespace Umbraco.Web.Routing
{
    /// <summary>
    /// Provides an implementation of <see cref="IContentFinder"/> that handles page url rewrites
    /// that are stored when moving, saving, or deleting a node.
    /// </summary>
    /// <remarks>
    /// <para>Assigns a permanent redirect notification to the request.</para>
    /// </remarks>
    public class ContentFinderByRedirectUrl : IContentFinder
    {
        private readonly IRedirectUrlService _redirectUrlService;

        public ContentFinderByRedirectUrl(IRedirectUrlService redirectUrlService)
        {
            _redirectUrlService = redirectUrlService;
        }

        /// <summary>
        /// Tries to find and assign an Umbraco document to a <c>PublishedContentRequest</c>.
        /// </summary>
        /// <param name="contentRequest">The <c>PublishedContentRequest</c>.</param>
        /// <returns>A value indicating whether an Umbraco document was found and assigned.</returns>
        /// <remarks>Optionally, can also assign the template or anything else on the document request, although that is not required.</remarks>
        public bool TryFindContent(PublishedContentRequest contentRequest)
        {
            var route = contentRequest.HasDomain
                ? contentRequest.Domain.ContentId + DomainHelper.PathRelativeToDomain(contentRequest.Domain.Uri, contentRequest.Uri.GetAbsolutePathDecoded())
                : contentRequest.Uri.GetAbsolutePathDecoded();

            var redirectUrl = _redirectUrlService.GetMostRecentRedirectUrl(route);

            if (redirectUrl == null)
            {
                LogHelper.Debug<ContentFinderByRedirectUrl>("No match for route: \"{0}\".", () => route);
                return false;
            }

            var content = contentRequest.RoutingContext.UmbracoContext.ContentCache.GetById(redirectUrl.ContentId);
            var url = content == null ? "#" : content.Url;
            if (url.StartsWith("#"))
            {
                LogHelper.Debug<ContentFinderByRedirectUrl>("Route \"{0}\" matches content {1} which has no url.",
                    () => route, () => redirectUrl.ContentId);
                return false;
            }

            LogHelper.Debug<ContentFinderByRedirectUrl>("Route \"{0}\" matches content {1} with url \"{2}\", redirecting.",
                () => route, () => content.Id, () => url);
            contentRequest.SetRedirectPermanent(url);
            return true;
        }
    }
}