namespace CarbonFootprint.Domain.Modules.Verification;

public enum VerificationWorkflowState
{
    Draft = 1,
    ReadinessFailed = 2,
    ReadyForReview = 3,
    Submitted = 4,
    InReview = 5,
    ChangesRequested = 6,
    Resubmitted = 7,
    InternallyApproved = 8,
    VerificationRequested = 9,
    UnderVerification = 10,
    Verified = 11,
    Rejected = 12,
    Published = 13,
    Superseded = 14,
    Expired = 15,
    Revoked = 16
}

public enum ReviewFindingSeverity
{
    Critical = 1,
    Major = 2,
    Minor = 3,
    Observation = 4
}

public enum ReviewFindingStatus
{
    Open = 1,
    Responded = 2,
    Resolved = 3,
    AcceptedRisk = 4,
    RejectedResponse = 5
}

public enum VerificationConclusion
{
    ReasonableAssurance = 1,
    LimitedAssurance = 2,
    Qualified = 3,
    Adverse = 4,
    UnableToConclude = 5
}

public sealed record WorkflowActor(
    string UserId,
    Guid OrganizationId,
    IReadOnlySet<string> Roles,
    bool HasMfa,
    IReadOnlySet<Guid> MateriallyEditedProjectVersionIds);

public sealed record WorkflowTransitionRequest(
    Guid ProjectVersionId,
    VerificationWorkflowState CurrentState,
    VerificationWorkflowState TargetState,
    WorkflowActor Actor,
    string CreatorUserId,
    bool ReadinessPassed,
    bool HasOpenBlockingFindings,
    bool HasVerificationRecord,
    bool HasSignedStatement,
    bool InputsChangedAfterApproval,
    string Reason,
    DateTimeOffset OccurredAt);

public sealed record WorkflowTransitionResult(
    VerificationWorkflowState PreviousState,
    VerificationWorkflowState CurrentState,
    bool InvalidatedPriorApproval,
    string AuditCode,
    DateTimeOffset OccurredAt,
    string ActorId,
    string Reason);

public sealed record ReviewFinding(
    Guid Id,
    Guid ProjectVersionId,
    int ReviewCycle,
    ReviewFindingSeverity Severity,
    string Category,
    string AffectedEntityType,
    Guid AffectedEntityId,
    string OwnerUserId,
    DateOnly? DueDate,
    ReviewFindingStatus Status,
    string Description,
    string Response,
    string Resolution,
    IReadOnlyList<Guid> EvidenceDocumentVersionIds,
    DateTimeOffset CreatedAt,
    string CreatedBy,
    DateTimeOffset? ResolvedAt,
    string ResolvedBy);

public sealed record ReviewCycle(
    Guid Id,
    Guid ProjectVersionId,
    int CycleNumber,
    Guid SubmittedSnapshotId,
    string SubmittedManifestSha256,
    DateTimeOffset SubmittedAt,
    string SubmittedBy,
    DateTimeOffset? CompletedAt,
    string CompletionNote);

public sealed record VerificationSamplingItem(
    Guid Id,
    string EntityType,
    Guid EntityId,
    string SamplingReason,
    string Procedure,
    string Result,
    IReadOnlyList<Guid> EvidenceDocumentVersionIds);

public sealed record VerificationRecord(
    Guid Id,
    Guid ProjectVersionId,
    Guid VerificationOrganizationId,
    string VerifierUserId,
    string VerifierQualification,
    string EngagementReference,
    string Scope,
    IReadOnlyList<VerificationSamplingItem> SamplingItems,
    IReadOnlyList<Guid> FindingIds,
    VerificationConclusion Conclusion,
    string ConclusionStatement,
    string SignedStatementSha256,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    string ManifestSha256)
{
    public bool HasValidSignedStatement =>
        SignedStatementSha256.Length == 64
        && SignedStatementSha256.All(Uri.IsHexDigit);
}

public static class VerificationWorkflowService
{
    private static readonly IReadOnlyDictionary<VerificationWorkflowState, IReadOnlySet<VerificationWorkflowState>> AllowedTransitions =
        new Dictionary<VerificationWorkflowState, IReadOnlySet<VerificationWorkflowState>>
        {
            [VerificationWorkflowState.Draft] = Set(VerificationWorkflowState.ReadinessFailed, VerificationWorkflowState.ReadyForReview),
            [VerificationWorkflowState.ReadinessFailed] = Set(VerificationWorkflowState.Draft, VerificationWorkflowState.ReadyForReview),
            [VerificationWorkflowState.ReadyForReview] = Set(VerificationWorkflowState.Submitted, VerificationWorkflowState.Draft),
            [VerificationWorkflowState.Submitted] = Set(VerificationWorkflowState.InReview, VerificationWorkflowState.ChangesRequested),
            [VerificationWorkflowState.InReview] = Set(VerificationWorkflowState.ChangesRequested, VerificationWorkflowState.InternallyApproved, VerificationWorkflowState.Rejected),
            [VerificationWorkflowState.ChangesRequested] = Set(VerificationWorkflowState.Resubmitted, VerificationWorkflowState.Rejected),
            [VerificationWorkflowState.Resubmitted] = Set(VerificationWorkflowState.InReview, VerificationWorkflowState.ChangesRequested),
            [VerificationWorkflowState.InternallyApproved] = Set(VerificationWorkflowState.VerificationRequested, VerificationWorkflowState.Draft, VerificationWorkflowState.Revoked),
            [VerificationWorkflowState.VerificationRequested] = Set(VerificationWorkflowState.UnderVerification, VerificationWorkflowState.Revoked),
            [VerificationWorkflowState.UnderVerification] = Set(VerificationWorkflowState.Verified, VerificationWorkflowState.ChangesRequested, VerificationWorkflowState.Rejected),
            [VerificationWorkflowState.Verified] = Set(VerificationWorkflowState.Published, VerificationWorkflowState.Draft, VerificationWorkflowState.Revoked),
            [VerificationWorkflowState.Rejected] = Set(VerificationWorkflowState.Draft),
            [VerificationWorkflowState.Published] = Set(VerificationWorkflowState.Superseded, VerificationWorkflowState.Expired, VerificationWorkflowState.Revoked),
            [VerificationWorkflowState.Superseded] = Set(VerificationWorkflowState.Revoked),
            [VerificationWorkflowState.Expired] = Set(VerificationWorkflowState.Revoked),
            [VerificationWorkflowState.Revoked] = Set()
        };

    private static readonly IReadOnlySet<VerificationWorkflowState> HighImpactTargets = Set(
        VerificationWorkflowState.InternallyApproved,
        VerificationWorkflowState.Verified,
        VerificationWorkflowState.Published,
        VerificationWorkflowState.Revoked,
        VerificationWorkflowState.Superseded);

    public static WorkflowTransitionResult Transition(WorkflowTransitionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!AllowedTransitions.TryGetValue(request.CurrentState, out var allowed)
            || !allowed.Contains(request.TargetState))
        {
            throw new InvalidOperationException($"Transition {request.CurrentState} -> {request.TargetState} is not allowed.");
        }

        if (string.IsNullOrWhiteSpace(request.Reason)
            && request.TargetState is VerificationWorkflowState.ChangesRequested
                or VerificationWorkflowState.Rejected
                or VerificationWorkflowState.Revoked
                or VerificationWorkflowState.Superseded)
        {
            throw new InvalidOperationException("This workflow transition requires a reason.");
        }

        if (HighImpactTargets.Contains(request.TargetState) && !request.Actor.HasMfa)
        {
            throw new InvalidOperationException("MFA is required for approval, verification, publication and revocation transitions.");
        }

        if (request.TargetState is VerificationWorkflowState.InternallyApproved or VerificationWorkflowState.Verified)
        {
            EnforceSeparationOfDuties(request);
        }

        if (request.TargetState is VerificationWorkflowState.ReadyForReview or VerificationWorkflowState.Submitted
            && !request.ReadinessPassed)
        {
            throw new InvalidOperationException("Inventory readiness validation must pass before review or submission.");
        }

        if (request.TargetState is VerificationWorkflowState.InternallyApproved or VerificationWorkflowState.Verified
            && request.HasOpenBlockingFindings)
        {
            throw new InvalidOperationException("Blocking findings must be resolved or explicitly accepted before approval or verification.");
        }

        if (request.TargetState == VerificationWorkflowState.Verified
            && (!request.HasVerificationRecord || !request.HasSignedStatement))
        {
            throw new InvalidOperationException("Verification requires a completed verification record and signed statement.");
        }

        if (request.TargetState == VerificationWorkflowState.Published
            && request.CurrentState != VerificationWorkflowState.Verified)
        {
            throw new InvalidOperationException("Only a verified inventory version can be published.");
        }

        var invalidated = request.InputsChangedAfterApproval
            && request.CurrentState is VerificationWorkflowState.InternallyApproved
                or VerificationWorkflowState.VerificationRequested
                or VerificationWorkflowState.UnderVerification
                or VerificationWorkflowState.Verified
                or VerificationWorkflowState.Published;
        if (invalidated && request.TargetState != VerificationWorkflowState.Draft)
        {
            throw new InvalidOperationException("Governed inputs changed after approval or verification. Create a new version or return to Draft.");
        }

        return new(
            request.CurrentState,
            request.TargetState,
            invalidated,
            $"WORKFLOW-{request.CurrentState.ToString().ToUpperInvariant()}-{request.TargetState.ToString().ToUpperInvariant()}",
            request.OccurredAt,
            request.Actor.UserId,
            request.Reason.Trim());
    }

    public static IReadOnlyList<ReviewFinding> ValidateFindingsForApproval(
        IReadOnlyList<ReviewFinding> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);
        return findings
            .Where(finding =>
                finding.Severity is ReviewFindingSeverity.Critical or ReviewFindingSeverity.Major
                && finding.Status is not ReviewFindingStatus.Resolved
                    and not ReviewFindingStatus.AcceptedRisk)
            .OrderBy(finding => finding.Severity)
            .ThenBy(finding => finding.DueDate)
            .ThenBy(finding => finding.Id)
            .ToArray();
    }

    public static ReviewFinding Respond(
        ReviewFinding finding,
        string actorId,
        string response,
        IReadOnlyList<Guid> evidenceDocumentVersionIds)
    {
        ArgumentNullException.ThrowIfNull(finding);
        if (finding.Status is ReviewFindingStatus.Resolved or ReviewFindingStatus.AcceptedRisk)
        {
            throw new InvalidOperationException("A closed finding cannot receive another response.");
        }

        if (string.IsNullOrWhiteSpace(response))
        {
            throw new InvalidOperationException("Finding response is required.");
        }

        return finding with
        {
            Status = ReviewFindingStatus.Responded,
            Response = response.Trim(),
            EvidenceDocumentVersionIds = evidenceDocumentVersionIds.Distinct().OrderBy(value => value).ToArray(),
            ResolvedBy = actorId
        };
    }

    public static ReviewFinding Resolve(
        ReviewFinding finding,
        WorkflowActor reviewer,
        string resolution,
        DateTimeOffset resolvedAt,
        bool acceptRisk = false)
    {
        ArgumentNullException.ThrowIfNull(finding);
        ArgumentNullException.ThrowIfNull(reviewer);
        if (!reviewer.Roles.Contains("Reviewer") && !reviewer.Roles.Contains("Administrator"))
        {
            throw new InvalidOperationException("Reviewer or administrator role is required to resolve a finding.");
        }

        if (string.IsNullOrWhiteSpace(resolution))
        {
            throw new InvalidOperationException("Finding resolution is required.");
        }

        if (acceptRisk && finding.Severity == ReviewFindingSeverity.Critical)
        {
            throw new InvalidOperationException("Critical findings cannot be accepted as residual risk.");
        }

        return finding with
        {
            Status = acceptRisk ? ReviewFindingStatus.AcceptedRisk : ReviewFindingStatus.Resolved,
            Resolution = resolution.Trim(),
            ResolvedAt = resolvedAt,
            ResolvedBy = reviewer.UserId
        };
    }

    public static VerificationRecord CompleteVerification(
        VerificationRecord record,
        WorkflowActor verifier,
        string creatorUserId,
        IReadOnlySet<string> materialEditors)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(verifier);
        if (!verifier.HasMfa)
        {
            throw new InvalidOperationException("MFA is required to complete verification.");
        }

        if (!verifier.Roles.Contains("Verifier") && !verifier.Roles.Contains("Administrator"))
        {
            throw new InvalidOperationException("Verifier role is required.");
        }

        if (string.Equals(verifier.UserId, creatorUserId, StringComparison.Ordinal)
            || materialEditors.Contains(verifier.UserId))
        {
            throw new InvalidOperationException("The creator or a material editor cannot verify the same inventory version.");
        }

        if (record.CompletedAt < record.StartedAt)
        {
            throw new InvalidOperationException("Verification completion time cannot precede the start time.");
        }

        if (record.SamplingItems.Count == 0)
        {
            throw new InvalidOperationException("Verification requires at least one sampling record.");
        }

        if (!record.HasValidSignedStatement)
        {
            throw new InvalidOperationException("Verification requires a valid SHA-256 for the signed statement.");
        }

        return record;
    }

    private static void EnforceSeparationOfDuties(WorkflowTransitionRequest request)
    {
        if (string.Equals(request.Actor.UserId, request.CreatorUserId, StringComparison.Ordinal)
            || request.Actor.MateriallyEditedProjectVersionIds.Contains(request.ProjectVersionId))
        {
            throw new InvalidOperationException("The inventory creator or material editor cannot approve or verify the same project version.");
        }

        var requiredRole = request.TargetState == VerificationWorkflowState.Verified
            ? "Verifier"
            : "Reviewer";
        if (!request.Actor.Roles.Contains(requiredRole)
            && !request.Actor.Roles.Contains("Administrator"))
        {
            throw new InvalidOperationException($"{requiredRole} role is required.");
        }
    }

    private static IReadOnlySet<VerificationWorkflowState> Set(params VerificationWorkflowState[] states) =>
        new HashSet<VerificationWorkflowState>(states);
}
