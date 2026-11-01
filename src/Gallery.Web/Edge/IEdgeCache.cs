namespace Gallery.Web.Edge;

/// <summary>
/// The owner request's only edge seam (FR-040): enqueue and return. Never blocks, never throws;
/// a full queue drops the work with <c>edge.dropped</c> (bounded lifetimes are the fallback).
/// </summary>
public interface IEdgeCache
{
    void Enqueue(EdgeWork work);
}
