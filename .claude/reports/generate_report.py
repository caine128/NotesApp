from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import getSampleStyleSheet, ParagraphStyle
from reportlab.lib.units import cm
from reportlab.lib import colors
from reportlab.platypus import (
    SimpleDocTemplate, Paragraph, Spacer, Table, TableStyle,
    PageBreak, HRFlowable, Preformatted
)
from reportlab.lib.enums import TA_LEFT, TA_CENTER
import os

OUTPUT = r"C:\Users\msi-nb\source\repos\caine128\NotesApp\.claude\reports\dotnet-skills-integration-report.pdf"

# ── Colour palette ────────────────────────────────────────────────────────────
DARK_BLUE   = colors.HexColor("#1A2B4A")
MID_BLUE    = colors.HexColor("#2D5FA8")
LIGHT_BLUE  = colors.HexColor("#EBF2FF")
CODE_BG     = colors.HexColor("#F4F4F5")
CODE_BORDER = colors.HexColor("#D4D4D8")
TABLE_HEAD  = colors.HexColor("#1A2B4A")
TABLE_ALT   = colors.HexColor("#F8FAFD")
RED_LIGHT   = colors.HexColor("#FEF2F2")
GREEN_LIGHT = colors.HexColor("#F0FDF4")
ORANGE      = colors.HexColor("#C2410C")
GREEN_DARK  = colors.HexColor("#15803D")
GREY_TEXT   = colors.HexColor("#52525B")
BORDER_GREY = colors.HexColor("#E4E4E7")

# ── Styles ────────────────────────────────────────────────────────────────────
base = getSampleStyleSheet()

def s(name, **kw):
    ps = ParagraphStyle(name, **kw)
    return ps

TITLE      = s("RTitle",   fontName="Helvetica-Bold",  fontSize=26, textColor=DARK_BLUE,
               spaceAfter=6, leading=32, alignment=TA_LEFT)
SUBTITLE   = s("RSub",     fontName="Helvetica",       fontSize=13, textColor=GREY_TEXT,
               spaceAfter=20, leading=18)
H1         = s("RH1",      fontName="Helvetica-Bold",  fontSize=16, textColor=DARK_BLUE,
               spaceBefore=20, spaceAfter=6, leading=22,
               borderPadding=(0,0,4,0))
H2         = s("RH2",      fontName="Helvetica-Bold",  fontSize=13, textColor=MID_BLUE,
               spaceBefore=14, spaceAfter=4, leading=18)
H3         = s("RH3",      fontName="Helvetica-BoldOblique", fontSize=11, textColor=DARK_BLUE,
               spaceBefore=10, spaceAfter=3, leading=16)
BODY       = s("RBody",    fontName="Helvetica",       fontSize=10, textColor="#1C1C1E",
               spaceAfter=6, leading=15)
BODY_SMALL = s("RSmall",   fontName="Helvetica",       fontSize=9,  textColor=GREY_TEXT,
               spaceAfter=4, leading=13)
BULLET     = s("RBullet",  fontName="Helvetica",       fontSize=10, textColor="#1C1C1E",
               spaceAfter=4, leading=15, leftIndent=16, firstLineIndent=-10)
BULLET2    = s("RBullet2", fontName="Helvetica",       fontSize=10, textColor=GREY_TEXT,
               spaceAfter=3, leading=14, leftIndent=30, firstLineIndent=-10)
CODE_STYLE = s("RCode",    fontName="Courier",         fontSize=8,  textColor="#1C1C1E",
               backColor=CODE_BG, leading=12, leftIndent=0)
LABEL_GREEN= s("LGreen",   fontName="Helvetica-Bold",  fontSize=9,  textColor=GREEN_DARK,
               backColor=GREEN_LIGHT, leading=13)
LABEL_RED  = s("LRed",     fontName="Helvetica-Bold",  fontSize=9,  textColor=ORANGE,
               backColor=RED_LIGHT,   leading=13)
CAPTION    = s("RCaption", fontName="Helvetica-Oblique",fontSize=8, textColor=GREY_TEXT,
               spaceAfter=8, leading=12, alignment=TA_CENTER)

def p(text, style=BODY): return Paragraph(text, style)
def sp(h=6):             return Spacer(1, h)
def hr():                return HRFlowable(width="100%", thickness=1,
                                           color=BORDER_GREY, spaceAfter=8, spaceBefore=4)
def code(text):
    lines = text.strip("\n")
    return Table(
        [[Preformatted(lines, CODE_STYLE)]],
        colWidths=[16.5*cm],
        style=TableStyle([
            ("BACKGROUND", (0,0), (-1,-1), CODE_BG),
            ("BOX",        (0,0), (-1,-1), 0.5, CODE_BORDER),
            ("LEFTPADDING",(0,0), (-1,-1), 8),
            ("RIGHTPADDING",(0,0),(-1,-1), 8),
            ("TOPPADDING", (0,0), (-1,-1), 6),
            ("BOTTOMPADDING",(0,0),(-1,-1),6),
        ])
    )

def grid(data, col_widths, header_row=True):
    ts = [
        ("FONTNAME",     (0,0), (-1,-1), "Helvetica"),
        ("FONTSIZE",     (0,0), (-1,-1), 9),
        ("ROWBACKGROUNDS",(0,1),(-1,-1), [colors.white, TABLE_ALT]),
        ("GRID",         (0,0), (-1,-1), 0.4, BORDER_GREY),
        ("VALIGN",       (0,0), (-1,-1), "TOP"),
        ("TOPPADDING",   (0,0), (-1,-1), 5),
        ("BOTTOMPADDING",(0,0),(-1,-1), 5),
        ("LEFTPADDING",  (0,0), (-1,-1), 7),
        ("RIGHTPADDING", (0,0),(-1,-1), 7),
    ]
    if header_row:
        ts += [
            ("BACKGROUND", (0,0), (-1,0), TABLE_HEAD),
            ("TEXTCOLOR",  (0,0), (-1,0), colors.white),
            ("FONTNAME",   (0,0), (-1,0), "Helvetica-Bold"),
            ("FONTSIZE",   (0,0), (-1,0), 9),
        ]
    rows = []
    for i, row in enumerate(data):
        rows.append([Paragraph(str(cell), BODY_SMALL) for cell in row])
    return Table(rows, colWidths=col_widths,
                 style=TableStyle(ts), hAlign="LEFT")

# ── Page template ─────────────────────────────────────────────────────────────
def on_page(canvas, doc):
    canvas.saveState()
    w, h = A4
    # header rule
    canvas.setStrokeColor(MID_BLUE)
    canvas.setLineWidth(2)
    canvas.line(2*cm, h - 1.6*cm, w - 2*cm, h - 1.6*cm)
    # footer
    canvas.setFont("Helvetica", 8)
    canvas.setFillColor(GREY_TEXT)
    canvas.drawString(2*cm, 1*cm, "Integration Report: dotnet/skills — NotesApp Pipeline")
    canvas.drawRightString(w - 2*cm, 1*cm, f"Page {doc.page}")
    canvas.restoreState()

# ── Content ───────────────────────────────────────────────────────────────────
story = []

# Cover block
story += [
    sp(30),
    p("Integration Report", TITLE),
    p("dotnet/skills &rarr; NotesApp Pipeline", SUBTITLE),
    HRFlowable(width="40%", thickness=3, color=MID_BLUE, spaceAfter=10),
    p("Prepared: April 2026 &nbsp;|&nbsp; Scope: Test Quality + Performance Audit", BODY_SMALL),
    sp(10),
]

# ─────────────────────────────────────────────────────────────────────────────
story += [PageBreak(), p("What This Report Covers", H1), hr()]
story += [
    p("Two skill clusters from the official <b>dotnet/skills</b> GitHub repository "
      "(github.com/dotnet/skills) have been evaluated against the NotesApp codebase. "
      "Everything else in that 87-skill repo is noise for this project. "
      "This report documents exactly which skills add value, how they integrate without "
      "creating overhead, and what changes concretely before and after.", BODY),
    sp(4),
    p("The five criteria used to evaluate each skill:", BODY),
    p("&bull; Better and more consistent coding sessions", BULLET),
    p("&bull; Token economy — no skill is loaded unless it earns its cost", BULLET),
    p("&bull; Better handling of the repository", BULLET),
    p("&bull; Better understanding of the codebase and alignment with best practices", BULLET),
    p("&bull; Knowing exactly when to include the human in the loop", BULLET),
    sp(10),
]

# ─────────────────────────────────────────────────────────────────────────────
story += [p("Repository Landscape", H1), hr()]
story += [
    p("The dotnet/skills repo contains <b>87 skills across 12 plugin categories</b>. "
      "The table below shows why most are irrelevant to NotesApp and which two clusters "
      "clear the bar.", BODY),
    sp(6),
    grid([
        ["Plugin", "Skills", "Relevance to NotesApp", "Decision"],
        ["dotnet-maui",            "8",  "Not a mobile project",                         "No"],
        ["dotnet-msbuild",         "14", "No build perf problems identified",             "No"],
        ["dotnet-template-engine", "4",  "Not building templates",                        "No"],
        ["dotnet-nuget (CPM)",     "1",  "One-time migration, not ongoing",               "No"],
        ["dotnet-ai (MCP)",        "5",  "Not building MCP servers",                      "No"],
        ["dotnet (core)",          "3",  "P/Invoke, scripting — not applicable",          "No"],
        ["dotnet-upgrade",         "6",  "Relevant when moving to .NET 9 — not yet",      "Later"],
        ["dotnet-aspnet",          "2",  "OpenTelemetry relevant only if tracing added",  "Later"],
        ["dotnet-data (EF Core)",  "1",  "Overlaps with existing efcore-patterns skill",  "No"],
        ["dotnet-experimental",    "4",  "Test quality — real gap in current pipeline",   "YES"],
        ["dotnet-test",            "1",  "test-anti-patterns — stable, pragmatic",        "YES"],
        ["dotnet-diag",            "1",  "analyzing-dotnet-performance — on-demand scan", "YES"],
    ],
    [4.5*cm, 1.8*cm, 6.5*cm, 2.2*cm]),
    sp(8),
]

# ─────────────────────────────────────────────────────────────────────────────
story += [PageBreak(), p("Cluster 1 — Test Quality (four skills)", H1), hr()]
story += [
    p("Your existing skill stack (<i>csharp-coding-standards</i>, <i>efcore-patterns</i>, etc.) "
      "tells Claude how to write production code. None of those skills say anything about "
      "whether the tests written <i>for</i> that code are effective. That is the gap.", BODY),
    sp(6),
    grid([
        ["Skill", "What it does", "When triggered"],
        ["exp-test-gap-analysis",
         "Pseudo-mutation: asks 'if I broke this line, would any test catch it?'",
         "After implementing a handler with complex branching"],
        ["exp-mock-usage-analysis",
         "Traces each mock setup through the production call path — finds dead setups and unreachable branches",
         "When a handler test has more than 3 mocks"],
        ["exp-test-smell-detection",
         "Formal audit: 19 smell categories (assertion-free, eager test, mystery guest…)",
         "Periodic suite health check, not per-feature"],
        ["test-anti-patterns (stable)",
         "Pragmatic: false confidence patterns, swallowed exceptions, always-true assertions",
         "After writing tests, as part of every review"],
    ],
    [3.8*cm, 7.2*cm, 4.5*cm]),
    sp(10),
]

# Gap examples
story += [p("Where the Gap Shows Up Today — Concrete Examples", H2)]

story += [
    p("<b>Example 1: Unreachable mock setup</b>", H3),
    p("From <i>RegisterDeviceCommandHandlerTests</i> — your <code>CreateSut()</code> "
      "pattern loads every mock setup for every test:", BODY),
    code("""private RegisterDeviceCommandHandler CreateSut()
{
    _currentUserServiceMock
        .Setup(x => x.GetUserIdAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(_currentUserId);

    _clockMock
        .Setup(x => x.UtcNow)
        .Returns(_utcNow);          // loaded for EVERY test

    return new RegisterDeviceCommandHandler(...);
}"""),
    sp(6),
    p("Consider a validation-failure test:", BODY),
    code("""[Fact]
public async Task Handle_WhenDeviceValidationFails_ReturnsDomainFailure()
{
    var sut = CreateSut();   // _clockMock.Setup runs here too
    var command = new RegisterDeviceCommand(invalidToken: "");

    var result = await sut.Handle(command, CancellationToken.None);

    result.IsFailed.Should().BeTrue();
}"""),
    sp(6),
    p("<b>exp-mock-usage-analysis</b> traces the production execution path: "
      "validation fails &rarr; early return &rarr; <code>ISystemClock.UtcNow</code> "
      "is never called. The <code>_clockMock</code> setup is dead for this test. "
      "It loads, does nothing, and silently passes. You would not discover this until "
      "you changed the production code, removed the <code>UtcNow</code> call, "
      "and the test still passed when it should not.", BODY),
    sp(8),
]

story += [
    p("<b>Example 2: Survived mutation — test does not verify what it claims</b>", H3),
    p("A typical multi-branch handler:", BODY),
    code("""public async Task<Result<NoteDetailDto>> Handle(UpdateNoteCommand command, CancellationToken ct)
{
    var userId = await _currentUserService.GetUserIdAsync(ct);
    var note   = await _noteRepository.GetByIdAsync(command.NoteId, ct);

    if (note is null || note.UserId != userId)
        return Result.Fail(new NotFoundError());        // early return A

    if (note.IsDeleted)
        return Result.Fail(new NotFoundError());        // early return B

    var domainResult = note.Update(command.Title, _clock.UtcNow);
    if (domainResult.IsFailed)
        return domainResult.ToResult<NoteDetailDto>();  // early return C

    // ... outbox + save
}"""),
    sp(6),
    p("The 'updating deleted note' test:", BODY),
    code("""[Fact]
public async Task Handle_WhenNoteIsDeleted_ReturnsNotFound()
{
    // arrange: note.IsDeleted = true
    var result = await sut.Handle(command, CancellationToken.None);

    result.IsFailed.Should().BeTrue();   // only assertion
}"""),
    sp(6),
    p("<b>exp-test-gap-analysis</b> runs pseudo-mutation on early return B:", BODY),
    p("&bull; <i>Mutation: remove the deleted check (early return B).</i> "
      "Handler proceeds to <code>note.Update()</code>. If the domain method also "
      "enforces IsDeleted, it returns a DomainFailure. <code>result.IsFailed</code> "
      "is still true — via a completely different code path. The test passes. "
      "The mutation <b>survived</b>.", BULLET),
    p("&bull; The assertion does not distinguish between NotFoundError from the deleted "
      "check vs DomainFailure from the domain method. This is a real test gap.", BULLET),
    sp(4),
    p("The recommended fix:", BODY),
    code("""// Assert the error type, not just IsFailed
result.Errors.Should().ContainSingle(e => e is NotFoundError);

// OR assert what did NOT happen
_noteRepositoryMock.Verify(
    x => x.Update(It.IsAny<Note>()),
    Times.Never);"""),
    sp(8),
]

story += [
    p("<b>Example 3: Assertion quality — loose clock assertion</b>", H3),
    p("From your Application.Tests assertion patterns:", BODY),
    code("""// Precise — good
dto.Title.Should().Be(command.Title);
dto.CreatedAtUtc.Should().BeOnOrAfter(before);
persisted!.UserId.Should().Be(userId);

// Loose — quality signal
dto.UpdatedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));"""),
    sp(6),
    p("The 1-minute tolerance exists because of clock drift between test setup and "
      "assertion. But your codebase already injects <code>ISystemClock</code> — "
      "you control the clock. <b>exp-assertion-quality</b> would flag "
      "<code>BeCloseTo(... FromMinutes(1))</code> as a weak assertion and suggest:", BODY),
    code("""_clockMock.Setup(x => x.UtcNow).Returns(_fixedUtcNow);

// Then:
dto.UpdatedAtUtc.Should().Be(_fixedUtcNow);  // exact, no tolerance"""),
    sp(10),
]

# Integration without noise
story += [p("How Integration Works Without Creating Noise", H2)]
story += [
    p("These skills are <b>not added to the default task flow</b>. "
      "Token cost is zero unless explicitly triggered.", BODY),
    sp(6),
    p("Current flow (unchanged):", BODY),
    code("pre-task → codebase-researcher → docs-researcher → plan → implement → simplify"),
    sp(6),
    p("Proposed additions — targeted, not always-on:", BODY),
    code("""After implement + simplify:
  └─► If new tests were written:
        └─► test-anti-patterns        (stable, quick pass — Critical/High only)
        └─► exp-mock-usage-analysis   (only if handler test uses >3 mocks)

Periodic (not per-task):
  └─► exp-test-smell-detection   (suite health check, once per major feature)
  └─► exp-test-gap-analysis      (handlers with complex branching / multiple early returns)"""),
    sp(6),
    p("The CLAUDE.md addition is surgical:", BODY),
    code("""## Tests — quality gate

After writing tests for a new handler:
- Run `test-anti-patterns` on the new test file (Critical/High only)
- Run `exp-mock-usage-analysis` if the handler test uses more than 3 mocks

For handlers with complex branching (multiple early returns, domain logic, outbox):
- Run `exp-test-gap-analysis` against the handler + its test file
- Not mandatory for every handler — use judgment based on complexity"""),
    sp(10),
]

# Before / After table
story += [PageBreak(), p("Before vs. After — What Actually Changes", H2)]
story += [
    grid([
        ["Aspect", "Before", "After"],
        ["Dead mock detection",
         "Discovered only when production code changes and tests don't break as expected",
         "Caught proactively when the test is written — 'this setup is unreachable for this test path'"],
        ["Error type precision",
         "Tests assert IsFailed — passes even if wrong error type returned",
         "Gap analysis flags: 'you have no test that distinguishes NotFoundError from DomainFailure here'"],
        ["Clock assertion quality",
         "BeCloseTo(... FromMinutes(1)) silently accepted",
         "Flagged: 'you control the clock via ISystemClock — use exact assertion'"],
        ["Naming consistency",
         "Mixed snake_case/PascalCase across Application.Tests and Api.IntegrationTests",
         "test-anti-patterns flags as Low-severity, surfaces for conscious decision"],
        ["Mutation survivors",
         "UpdateNoteCommandHandlerTests covers deleted path but assertion doesn't distinguish which failure path triggered",
         "exp-test-gap-analysis surfaces 'removing the deleted check doesn't break your test — caught downstream'; you decide if acceptable"],
        ["Over-mocking in shared setup",
         "CreateSut() loads all setups for all tests, including ones irrelevant to the specific test",
         "exp-mock-usage-analysis identifies which setups are dead per test, suggests per-test setup for specificity"],
    ],
    [3.5*cm, 5.5*cm, 6.0*cm]),
    sp(10),
]

# ─────────────────────────────────────────────────────────────────────────────
story += [PageBreak(), p("Cluster 2 — Performance Anti-Pattern Audit", H1), hr()]
story += [
    p("<b>analyzing-dotnet-performance</b> (dotnet-diag plugin) is a scan tool, "
      "not a style guide. It reads code and flags ~50 specific patterns by severity.", BODY),
    sp(6),
    grid([
        ["Severity", "Examples"],
        ["Critical (deadlocks, >10x regression)",
         ".Result or .Wait() on async, async void, Thread.Sleep in async code"],
        ["Moderate (2–10x improvement opportunity)",
         "string.Format in hot loops, ToList() before Where(), Regex compiled per-call instead of cached, List<T> where IEnumerable<T> avoids copy"],
        ["Info (applicable pattern, non-critical path)",
         "LINQ over-chaining, Dictionary with default equality comparer where custom would help"],
    ],
    [5.5*cm, 10.0*cm]),
    sp(8),
]

story += [p("How It Differs from Your Existing Skills", H2)]
story += [
    p("Your existing <i>csharp-type-design-performance</i> and <i>csharp-coding-standards</i> "
      "are <b>prescriptive</b> — they tell Claude how to write new code. "
      "<i>analyzing-dotnet-performance</i> is <b>diagnostic</b> — it scans existing code "
      "for violations.", BODY),
    sp(6),
    grid([
        ["Skill", "Orientation", "Example output"],
        ["csharp-type-design-performance",
         "Prescriptive — write this way",
         "'When writing a new method, defer .ToList() to the end'"],
        ["analyzing-dotnet-performance",
         "Diagnostic — scan existing code",
         "'In NotesQueryHandler.cs line 47, .ToList() before .Where() materialises the entire collection. Moderate finding.'"],
    ],
    [4.5*cm, 4.5*cm, 6.5*cm]),
    sp(8),
]

story += [p("When to Use It (and When Not To)", H2)]
story += [
    p("<b>Use it for:</b>", BODY),
    p("&bull; A dedicated perf pass when a feature area shows slowness", BULLET),
    p("&bull; Pre-release audit of a handler that touches large or unbounded collections", BULLET),
    p("&bull; Reviewing the Worker — outbox processing iterates collections", BULLET),
    sp(4),
    p("<b>Do not add it to the default task flow.</b> It is a point-in-time scan, "
      "not a per-commit gate. The CLAUDE.md entry:", BODY),
    code("""## Performance audit

Run `analyzing-dotnet-performance` when:
- A handler iterates or queries collections of unbounded size
- The Worker outbox processing loop is being modified
- A query handler is identified as slow in production

Do not run it as a default step — it is a targeted diagnostic, not a style gate."""),
    sp(8),
]

story += [p("Before vs. After", H2)]
story += [
    grid([
        ["Before", "After"],
        ["Performance issues caught only when someone notices slowness in production",
         "Systematic scan available on-demand with structured Critical/Moderate/Info triage"],
        ["csharp-type-design-performance prevents new anti-patterns but does not scan existing code",
         "Existing code can be audited against the same standards on demand"],
        ["'Is this query handler efficient?' answered by reading code manually",
         "Answered by running the skill and receiving a structured severity report with file/line references"],
    ],
    [7.5*cm, 8.0*cm]),
    sp(10),
]

# ─────────────────────────────────────────────────────────────────────────────
story += [PageBreak(), p("What Does Not Get Added, and Why", H1), hr()]
story += [
    p("Explicit about the line:", BODY),
    sp(6),
    grid([
        ["dotnet/skills area", "Decision", "Reason"],
        ["MAUI (8 skills)",            "No",      "Not a mobile project"],
        ["MSBuild (14 skills)",        "No",      "No build performance problems identified"],
        ["Template engine (4 skills)", "No",      "Not building templates"],
        ["dotnet-upgrade (6 skills)",  "Not yet", "Relevant when upgrading to .NET 9 — flag then"],
        ["dotnet-aspnet / OpenTelemetry","Later", "Relevant only if tracing infrastructure is added"],
        ["dotnet-data / EF Core optimising","No", "Already covered by efcore-patterns — overlaps without adding new ground"],
        ["dotnet-ai / MCP (5 skills)", "No",      "Not building AI infrastructure"],
        ["convert-to-cpm",             "No",      "One-time migration — not an ongoing session concern"],
        ["exp-simd-vectorization",     "No",      "Not applicable to a web API notes app"],
        ["exp-test-maintainability",   "No",      "test-anti-patterns already covers the relevant overlap; adding both scans the same ground twice"],
    ],
    [4.5*cm, 2.0*cm, 9.0*cm]),
    sp(10),
]

# ─────────────────────────────────────────────────────────────────────────────
story += [p("Summary: Net Change to the Pipeline", H1), hr()]
story += [
    p("Two additions, both conditional:", BODY),
    sp(4),
    p("<b>1. Test quality gate</b> — <i>test-anti-patterns</i> + "
      "<i>exp-mock-usage-analysis</i> + <i>exp-test-gap-analysis</i>", H3),
    p("Triggered after writing tests for handlers with branching logic. "
      "Not in every task. The CLAUDE.md entry makes it explicit when to trigger each one.", BODY),
    sp(4),
    p("<b>2. Performance audit</b> — <i>analyzing-dotnet-performance</i>", H3),
    p("Triggered on-demand for perf-sensitive areas. Not in the default flow.", BODY),
    sp(8),
    p("The <i>exp-test-smell-detection</i> and <i>exp-assertion-quality</i> skills are "
      "worth keeping available but are better used as a periodic audit (every few features) "
      "rather than per-task triggers — no CLAUDE.md entry needed for them.", BODY),
    sp(4),
    p("Nothing else from the 87-skill repo is relevant to this codebase right now.", BODY),
    sp(20),
    HRFlowable(width="100%", thickness=2, color=MID_BLUE, spaceAfter=8),
    p("NotesApp &nbsp;|&nbsp; dotnet/skills integration analysis &nbsp;|&nbsp; April 2026",
      BODY_SMALL),
]

# ── Build ─────────────────────────────────────────────────────────────────────
doc = SimpleDocTemplate(
    OUTPUT,
    pagesize=A4,
    leftMargin=2*cm, rightMargin=2*cm,
    topMargin=2.2*cm, bottomMargin=2*cm,
    title="Integration Report: dotnet/skills — NotesApp Pipeline",
    author="Claude Code",
    subject="Test Quality + Performance Audit Skills Integration",
)

doc.build(story, onFirstPage=on_page, onLaterPages=on_page)
print(f"PDF written to: {OUTPUT}")
