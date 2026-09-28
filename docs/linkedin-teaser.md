Most AI coding content measures how fast code shows up. For safety-critical, mission-critical and high-availability systems, that's the wrong thing to measure.

I wrote up how I use Claude Code to take a microservice from idea to production when correctness matters more than speed:

→ Teach Claude to ask before it assumes. A confident guess is the most expensive defect an AI can produce. Write down exactly when Claude has to stop and ask: failure behavior, time zones, idempotency, new dependencies, contract changes. Each answer goes into the story or a design decision record, not a chat log.

→ User stories with real depth. Every acceptance criterion is tagged in Gherkin so tests, code and reviews can point back to it.

→ Pure, deterministic code. A functional core behind a thin imperative shell. Types that can't hold invalid values, and failures returned as values instead of thrown.

→ Skills and enterprise guardrails. Skills package repeatable procedures. Path-scoped rules apply security and compliance standards where they're relevant. Managed settings keep a human in the loop, keep secrets unread, restrict network access and log every action.

→ Test batteries, not just tests. Unit, property-based, mutation, integration, contract, end-to-end and resilience tests, each running as a pipeline gate.

→ Azure DevOps pipelines as code. Checked into GitHub, built once, and the same build promoted through every environment.

→ Code reviews that lead to action. Every finding names an input that breaks the code and the failing test that proves it.

→ A person reads every line. AI-written code reads fluently, which makes subtle mistakes easy to miss.

Claude drafts and asks. Tests and guardrails verify. Engineers decide what "correct" means.

Every code sample in the article compiles.

Full article: [link]

#ClaudeCode #SoftwareEngineering #DevOps #AzureDevOps #MissionCritical #dotnet
