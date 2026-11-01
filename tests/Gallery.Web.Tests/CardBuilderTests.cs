using Gallery.Web.Data;
using Gallery.Web.Library;
using Gallery.Web.Tests.Fixtures;
using MongoDB.Bson;

namespace Gallery.Web.Tests;

/// <summary>009 T009: <see cref="CardBuilder.Build"/>, the one card function (data-model "GalleryCard").</summary>
public class CardBuilderTests
{
    private static readonly ImageDoc A = SeedShapes.UploadedImage(id: "aaaaaaaaaaaaaaaaaaaaaaaaaa", title: "A");
    private static readonly ImageDoc B = SeedShapes.UploadedImage(id: "bbbbbbbbbbbbbbbbbbbbbbbbbb", title: "B");
    private static readonly ImageDoc C = SeedShapes.UploadedImage(id: "cccccccccccccccccccccccccc", title: "C");

    private static GalleryDoc G(string? pinned, params string[] items) => new()
    {
        Id = "gggggggggggggggggggggggggg",
        Slug = "harbour-light",
        Title = "Harbour Light",
        Visibility = "public",
        Items = [.. items],
        PinnedCover = pinned,
    };

    private static void AssertCover(ImageDoc expected, GalleryCardDoc card)
    {
        Assert.NotNull(card.Cover);
        Assert.Equal(expected.Id, card.Cover.ImageId);
        Assert.Equal(expected.Width, card.Cover.Width);
        Assert.Equal(expected.Height, card.Cover.Height);
        Assert.Equal(expected.Placeholder.ToBsonDocument(), card.Cover.Placeholder.ToBsonDocument());
        Assert.Equal(
            new BsonArray(expected.Derivatives.Select(d => d.ToBsonDocument())),
            new BsonArray(card.Cover.Derivatives.Select(d => d.ToBsonDocument())));
    }

    [Fact]
    public void Pinned_cover_is_the_cover()
    {
        var g = G(C.Id, A.Id, B.Id, C.Id);
        Assert.Equal(C.Id, CardBuilder.CoverId(g));

        var card = CardBuilder.Build(g, C);

        Assert.Equal("gggggggggggggggggggggggggg", card.GalleryId);
        Assert.Equal("harbour-light", card.Slug);
        Assert.Equal("Harbour Light", card.Title);
        Assert.Equal(3, card.ImageCount);
        AssertCover(C, card);
    }

    [Fact]
    public void Without_a_pin_the_first_item_is_the_cover()
    {
        var g = G(null, B.Id, A.Id);
        Assert.Equal(B.Id, CardBuilder.CoverId(g));

        var card = CardBuilder.Build(g, B);

        Assert.Equal(2, card.ImageCount);
        AssertCover(B, card);
    }

    [Fact]
    public void Empty_gallery_has_no_cover_and_count_0()
    {
        var g = G(null);
        Assert.Null(CardBuilder.CoverId(g));

        var card = CardBuilder.Build(g, null);

        Assert.Equal(0, card.ImageCount);
        Assert.Null(card.Cover);
        Assert.False(card.ToBsonDocument()["cover"].IsBsonDocument);
    }

    [Fact]
    public void Pinned_cover_removed_from_items_falls_back_to_the_first_item()
    {
        var g = G(C.Id, A.Id, B.Id); // C was pinned, then removed from items
        Assert.Equal(A.Id, CardBuilder.CoverId(g));

        var card = CardBuilder.Build(g, A);

        Assert.Equal(2, card.ImageCount);
        AssertCover(A, card);
    }

    [Fact]
    public void A_cover_that_is_not_the_gallery_cover_is_refused()
    {
        var g = G(null, A.Id, B.Id);
        var ex = Assert.Throws<ArgumentException>(() => CardBuilder.Build(g, B));
        Assert.StartsWith($"Image {B.Id} is not the cover of gallery {g.Id} (expected {A.Id}).", ex.Message);
    }

    [Fact]
    public void Card_shape_is_the_one_the_public_pages_read()
    {
        var bson = CardBuilder.Build(G(null, A.Id), A).ToBsonDocument();
        Assert.Equal(new[] { "galleryId", "slug", "title", "imageCount", "cover" }, bson.Names.ToArray());
        Assert.Equal(new[] { "imageId", "width", "height", "placeholder", "derivatives" },
            bson["cover"].AsBsonDocument.Names.ToArray());
    }
}
