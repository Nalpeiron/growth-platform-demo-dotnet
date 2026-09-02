using NalpeironGrowthPlatformDemo.Domain;
using Xunit;

namespace NalpeironGrowthPlatformDemo.Tests.Domain;

public sealed class ReferenceIdTests
{
    [Fact]
    public void ForCustomer_WhenCalled_ReturnsPrefixedIdWithin32Chars()
    {
        // act
        var id = ReferenceId.ForCustomer();

        // assert
        Assert.StartsWith(ReferenceId.Prefix, id);
        Assert.Equal(32, id.Length);
    }

    [Fact]
    public void ForOrder_WithCustomerName_ReturnsPrefixedIdWithin50CharsEndingWithSlug()
    {
        // act
        var id = ReferenceId.ForOrder("Acme Corp");

        // assert
        Assert.StartsWith(ReferenceId.Prefix, id);
        Assert.True(id.Length <= 50, $"length was {id.Length}");
        Assert.EndsWith("acme-corp", id);
    }

    [Fact]
    public void ForOrder_WithVeryLongCustomerName_TruncatesIdTo50Chars()
    {
        // arrange
        var customerName = new string('x', 200);

        // act
        var id = ReferenceId.ForOrder(customerName);

        // assert
        Assert.StartsWith(ReferenceId.Prefix, id);
        Assert.Equal(50, id.Length);
    }

    [Theory]
    [InlineData("Acme Corp", "acme-corp")]
    [InlineData("  Hello, World!  ", "hello--world")]
    [InlineData("///", "customer")]
    [InlineData("", "customer")]
    public void Slug_WithAnyInput_LowercasesAndReplacesNonAlphanumerics(string input, string expected)
    {
        // act
        var result = ReferenceId.Slug(input);

        // assert
        Assert.Equal(expected, result);
    }
}
