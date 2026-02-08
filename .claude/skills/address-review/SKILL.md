---
name: address-review
description: Address all code review comments on a PR. Assesses each comment, replies, reacts, and resolves threads.
argument-hint: [pr-number]
allowed-tools: Bash(gh *), Bash(git add *), Bash(git commit *), Bash(git push *), Bash(dotnet build *), Read, Grep, Glob, Edit, Write, WebFetch
---

# Address Code Review Comments

You are addressing code review comments on PR #$ARGUMENTS.

## Step 0: Wait for reviews to arrive

Bot reviewers (Copilot, Sentry, Codex, etc.) take time to post their comments after a commit is pushed. Before processing, wait until reviews have landed:

1. Check the timestamp of the latest commit on the PR:
   ```bash
   gh pr view $ARGUMENTS --json commits --jq '.commits[-1].committedDate'
   ```
2. If less than 5 minutes have passed since that commit, wait and re-check for new comments periodically (every 30–60 seconds).
3. Once 5 minutes have passed since the last commit with no new comments arriving, proceed to Step 1.

This ensures you don't start processing before all reviewers have had a chance to comment.

## Step 1: Gather context

Fetch the PR details and all review comments:

```bash
gh pr view $ARGUMENTS --json title,body,headRefName,baseRefName
gh api repos/{owner}/{repo}/pulls/$ARGUMENTS/comments --paginate
gh api repos/{owner}/{repo}/pulls/$ARGUMENTS/reviews --paginate
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
   gh api repos/{owner}/{repo}/pulls/$ARGUMENTS/comments -f body="..." -F in_reply_to={comment_id}
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
   gh api repos/{owner}/{repo}/pulls/$ARGUMENTS/comments -f body="..." -F in_reply_to={comment_id}
   ```
3. **Resolve the thread**:
   ```bash
   gh api graphql -f query='mutation { resolveReviewThread(input: {threadId: "THREAD_NODE_ID"}) { thread { isResolved } } }'
   ```

### C. If you're unsure whether the comment is valid:
1. **Reply** with your analysis and conclusion, explaining what you found and why you're uncertain.
2. **Do NOT resolve the thread** — leave it open for the reviewer to follow up.

## Step 4: Commit and push (if changes were made)

If any code changes were made:

1. Stage only the files you changed.
2. Commit with a message like: `address review: <brief summary of changes>`
3. Push to the PR branch.

## Step 5: Summary

After processing all comments, post a summary as a PR comment and also display it to the user:

```bash
gh pr comment $ARGUMENTS --body "$(cat <<'EOF'
## Review comments addressed

| # | Comment | Author | Verdict | Action |
|---|---------|--------|---------|--------|
| 1 | ... | ... | ... | ... |

EOF
)"
```

## Important rules

- **Process EVERY comment** - don't skip any, even bot comments.
- **Always reply** - every comment gets a response explaining your assessment.
- **Always react** - thumbs-up for accepted, thumbs-down for rejected.
- **Resolve when confident** - resolve threads you've accepted or rejected. Leave uncertain threads open.
- When replying, be respectful and concise. If rejecting, explain *why* clearly.
- Get the correct `{owner}/{repo}` from `gh repo view --json nameWithOwner -q .nameWithOwner`.
- To find the GraphQL thread node ID for resolving, use a paginated query (use `after` cursor if `hasNextPage` is true):
  ```bash
  gh api graphql -f query='query { repository(owner:"{owner}", name:"{repo}") { pullRequest(number:$ARGUMENTS) { reviewThreads(first:100) { pageInfo { hasNextPage endCursor } nodes { id isResolved comments(first:1) { nodes { body databaseId } } } } } } }'
  ```
  Match threads by the `databaseId` of the first comment in each thread. If `hasNextPage` is true, repeat the query with `after:"{endCursor}"` to fetch remaining threads.
