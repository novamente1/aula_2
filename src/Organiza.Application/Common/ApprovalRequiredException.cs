namespace Organiza.Application.Common;

public sealed class ApprovalRequiredException(string operation)
    : InvalidOperationException($"A operação '{operation}' exige aprovação explícita do usuário.");

public sealed class UnsafePathException(string path, int length, string? details = null)
    : InvalidOperationException($"O caminho possui {length} caracteres e ultrapassa o limite operacional de 240. " +
                                $"{details ?? "Encurte o nome ou reduza a profundidade das pastas."} Caminho: {path}");
