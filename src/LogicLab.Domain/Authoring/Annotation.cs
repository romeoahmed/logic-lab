namespace LogicLab.Domain.Authoring;

public enum AnnotationAlignment
{
    Start,
    Center,
    End,
}

public sealed record AnnotationValue(
    string Text,
    GridPoint Position,
    AnnotationAlignment Alignment);

public sealed class Annotation
{
    internal Annotation(AnnotationId id, AnnotationValue value)
    {
        Id = id;
        Text = value.Text;
        Position = value.Position;
        Alignment = value.Alignment;
    }

    public AnnotationId Id { get; }

    public string Text { get; }

    public GridPoint Position { get; }

    public AnnotationAlignment Alignment { get; }

    internal Annotation WithValue(AnnotationValue value)
    {
        return new Annotation(Id, value);
    }

    internal Annotation WithPosition(GridPoint position)
    {
        return new Annotation(Id, new AnnotationValue(Text, position, Alignment));
    }
}
