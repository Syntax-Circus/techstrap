using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using TechStrap.Application.Agents;
using TechStrap.Application.Content;
using TechStrap.Application.Persistence;
using TechStrap.Domain.Agents;
using TechStrap.Domain.Knowledge;
using TechStrap.Domain.Products;

namespace TechStrap.Application.Tests.Knowledge;

/// <summary>Substitutes and builders the KB handler tests share: a signed-in active agent, two products, and articles and categories with known versions.</summary>
internal sealed class KbFixture
{
    public KbFixture()
    {
        Agent = Agent.Create("sam", "Sam", "sam@example.com", AgentRole.Agent, Clock).Value;
        Claims.Current.Returns(new AgentClaims("sam", "Sam", "sam@example.com", AgentRole.Agent));
        Agents.GetBySubjectAsync("sam", Arg.Any<CancellationToken>()).Returns(Agent);
        Products.GetByIdAsync(Orbitly.Id, Arg.Any<CancellationToken>()).Returns(Orbitly);
        Products.GetByIdAsync(Paperplane.Id, Arg.Any<CancellationToken>()).Returns(Paperplane);
    }

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 5, 9, 0, 0, TimeSpan.Zero));

    public ICurrentAgentClaims Claims { get; } = Substitute.For<ICurrentAgentClaims>();

    public IAgentRepository Agents { get; } = Substitute.For<IAgentRepository>();

    public IProductRepository Products { get; } = Substitute.For<IProductRepository>();

    public IKbRepository KnowledgeBase { get; } = Substitute.For<IKbRepository>();

    /// <summary>The KB renderer substitute: IsTooComplex is false unless a test says otherwise.</summary>
    public IKbContentRenderer Renderer { get; } = Substitute.For<IKbContentRenderer>();

    public Agent Agent { get; }

    public Product Orbitly { get; } = Product.Create("orbitly", "Orbitly", "ORB", null, new FakeTimeProvider()).Value;

    public Product Paperplane { get; } = Product.Create("paperplane", "Paperplane", "PPL", null, new FakeTimeProvider()).Value;

    /// <summary>A stored category (version 3) that the repository substitute returns by id.</summary>
    public KbCategory StoredCategory(Guid? productId, string slug = "account")
    {
        var category = KbCategory.Restore(Guid.CreateVersion7(), productId, "Account", slug, null, 1, 3);
        KnowledgeBase.GetCategoryAsync(category.Id, Arg.Any<CancellationToken>()).Returns(category);
        return category;
    }

    /// <summary>A stored article (version 5) that the repository substitute returns by id.</summary>
    public KbArticle StoredArticle(
        KbArticleStatus status = KbArticleStatus.Draft, Guid? productId = null, Guid? categoryId = null, string slug = "reset-password", DateTimeOffset? publishedAt = null)
    {
        var article = KbArticle.Restore(
            Guid.CreateVersion7(), productId, categoryId, slug, "Reset your password", "Short", "Steps.", status, Agent.Id, Clock.GetUtcNow(), Clock.GetUtcNow(), publishedAt, 5);
        KnowledgeBase.GetArticleAsync(article.Id, Arg.Any<CancellationToken>()).Returns(article);
        return article;
    }
}
