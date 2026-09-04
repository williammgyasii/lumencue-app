## 1. Failing tests (TDD)

- [x] 1.1 Add chrome tests that AI Listening clips to the rail, the transcript box has a bounded height and sticks to latest, Suggestions does not show Start when off, and Paraphrases is hidden. Verify they fail because the flags do not exist

## 2. Chrome flags

- [x] 2.1 Add the flags and `TranscriptBoxHeight` on `OperatorWorkspaceChrome`. Verify task 1.1 passes

## 3. Operator UI

- [x] 3.1 Clip the right-rail AI panel (`MinHeight="0"`, header/`*` body scroll) and make the transcript a mini-scroll that sticks to the latest words. Verify the panel no longer overflows and new transcript stays in view
- [x] 3.2 Hide the Suggestions-tab Start Listening / off prompt; keep the scripture list. Hide the Paraphrases subtab and its pane; snap off that tab index. Verify Bible still has Scripture, AI Suggestions, and Find Scripture
