namespace FocusReader;

internal enum CursorChoice
{
    Hand,
    Arrow,
    Cross,
    UpArrow,
    IBeam,
    Help,
    Custom,
    Unchanged,
}

internal sealed record CursorOption(CursorChoice Value, string Label)
{
    public override string ToString() => Label;
}

internal static class CursorChoices
{
    public static IReadOnlyList<CursorOption> All { get; } =
    [
        new(CursorChoice.Hand, "Mão (indicador apontando)"),
        new(CursorChoice.Arrow, "Seta padrão"),
        new(CursorChoice.Cross, "Cruz"),
        new(CursorChoice.UpArrow, "Seta para cima"),
        new(CursorChoice.IBeam, "Barra de texto (I-beam)"),
        new(CursorChoice.Help, "Ajuda (seta com ?)"),
        new(CursorChoice.Custom, "Arquivo personalizado (.cur / .ani)"),
        new(CursorChoice.Unchanged, "Não alterar o cursor"),
    ];

    public static int? SystemCursorId(CursorChoice choice) => choice switch
    {
        CursorChoice.Hand => 32649,
        CursorChoice.Arrow => 32512,
        CursorChoice.Cross => 32515,
        CursorChoice.UpArrow => 32516,
        CursorChoice.IBeam => 32513,
        CursorChoice.Help => 32651,
        _ => null,
    };

    public static Cursor? WinFormsCursor(CursorChoice choice) => choice switch
    {
        CursorChoice.Hand => Cursors.Hand,
        CursorChoice.Arrow => Cursors.Arrow,
        CursorChoice.Cross => Cursors.Cross,
        CursorChoice.UpArrow => Cursors.UpArrow,
        CursorChoice.IBeam => Cursors.IBeam,
        CursorChoice.Help => Cursors.Help,
        _ => null,
    };
}
