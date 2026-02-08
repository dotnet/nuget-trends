---
name: address-review
description: Address all code review comments on a PR. Assesses each comment, replies, reacts, and resolves threads.
argument-hint: [pr-number]
allowed-tools: Bash(gh *), Read, Grep, Glob, Edit, Write, WebFetch
---

# Address Code Review Comments

You are addressing code review comments on PR #$ARGUMENTS.

## Step 1: Gather context

Fetch the PR details and all review comments:

```bash
gh pr view $ARGUMENTS --json title,body,headRefName,baseRefName
gh api repos/{owner}/{repo}/pulls/$ARGUMENTS/comments --paginate
gh api repos/{owner}/{repo}/pulls/$ARGUMENTS/reviews --paginate
gh api repos/{owner}/{repo}/issues/$ARGUMENTS/comments --paginate
```

Also fetch the current diff so you understand the changes being reviewed:

```bash
gh pr diff $ARGUMENTS
```

## Step 2: Assess each comment

For every review comment (from bots or humans), evaluate it:

1. **Read the comment** carefully, including any suggested code changes.
2. **Read the relevant source file(s)** at the lines being discussed to understand the full context.
3. **Determine validity**:
   - Is the feedback correct and actionable?
   - Is it a false positive from a bot?
   - Is it a style preference vs a real issue?
   - Is it already addressed or outdated?

## Step 3: Act on each comment

For **each** comment, do ALL of the following:

### A. If the comment is valid and actionable:
1. **Make the code change** in the local working tree.
2. **React with thumbs-up** (+1) to acknowledge:
   ```bash
   gh api repos/{owner}/{repo}/pulls/comments/{comment_id}/reactions -f content='+1'
   ```
3. **Reply** explaining what you changed:
   ```bash
   gh api repos/{owner}/{repo}/pulls/$ARGUMENTS/comments -f body="..." -f in_reply_to={comment_id}
   ```
4. **Resolve the thread**:
   ```bash
   gh api graphql -f query='mutation { resolveReviewThread(input: {threadId: "THREAD_NODE_ID"}) { thread { isResolved } } }'
   ```

### B. If the comment is not valid or not actionable:
1. **React with thumbs-down** (-1):
   ```bash
   gh api repos/{owner}/{repo}/pulls/comments/{comment_id}/reactions -f content='-1'
   ```
2. **Reply** explaining why the feedback was rejected with a clear, respectful rationale:
   ```bash
   gh api repos/{owner}/{repo}/pulls/$ARGUMENTS/comments -f body="..." -f in_reply_to={comment_id}
   ```
3. **Resolve the thread** (so the PR is ready to merge):
   ```bash
   gh api graphql -f query='mutation { resolveReviewThread(input: {threadId: "THREAD_NODE_ID"}) { thread { isResolved } } }'
   ```

## Step 4: Commit and push (if changes were made)

If any code changes were made:

1. Stage only the files you changed.
2. Commit with a message like: `address review: <brief summary of changes>`
3. Push to the PR branch.

## Step 5: Summary

After processing all comments, provide a summary table:

| # | Comment | Author | Verdict | Action |
|---|---------|--------|---------|--------|

## Important rules

- **Process EVERY comment** - don't skip any, even bot comments.
- **Always reply** - every comment gets a response explaining your assessment.
- **Always react** - thumbs-up for accepted, thumbs-down for rejected.
- **Always resolve** - resolve every thread you've reviewed so the PR is clean.
- When replying, be respectful and concise. If rejecting, explain *why* clearly.
- Get the correct `{owner}/{repo}` from `gh repo view --json nameWithOwner -q .nameWithOwner`.
- To find the GraphQL thread node ID for resolving, use:
  ```bash
  gh api graphql -f query='query { repository(owner:"{owner}", name:"{repo}") { pullRequest(number:$ARGUMENTS) { reviewThreads(first:100) { nodes { id isResolved comments(first:1) { nodes { body databaseId } } } } } } }'
  ```
  Match threads by the `databaseId` of the first comment in each thread.
