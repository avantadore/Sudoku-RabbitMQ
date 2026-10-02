namespace SudokuRabbitMQ.Core;

/// <summary>Which kind of unit: a row, a column or a box. Each kind has nine units, numbered 1–9.</summary>
public enum UnitKind
{
    Row,
    Column,
    Box,
}
