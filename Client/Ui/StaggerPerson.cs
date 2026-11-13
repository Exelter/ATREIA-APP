namespace AionDPS.Ui;

/// <summary>A player of the group as the stagger card names them: their id in the fight, name and class.</summary>
public sealed record StaggerPerson(int Id, string Name, string ClassName);
