# State machines

Impasto's explicit and implicit state machines: their states, the invariants they keep, how they move between states, and which invalid states the code can still reach. Update the entry for a machine whenever you change its owner code. Symbols are cited instead of line numbers.

**Status legend:** **fixed** (with the regression test that pins it), **fixed, untested** (fixed, but needs a GTK main loop or a real window to test), **open** (with the reason).

## Index

- Pointer and tools
  1. [ToolManager pointer gesture](#1-toolmanager-pointer-gesture)
  2. [Per-tool drag state](#2-per-tool-drag-state)
  3. [Shape edit engine](#3-shape-edit-engine)
- Document state
  4. [Document history and undo/redo](#4-document-history-and-undoredo)
  5. [Autosave](#5-autosave)
  6. [Active document](#6-active-document)
- Effects
  7. [Live preview session](#7-live-preview-session)
  8. [Effect nodes](#8-effect-nodes)
- App shell
  9. [Close, quit and save](#9-close-quit-and-save)
  10. [Layer Properties dialog](#10-layer-properties-dialog)
  11. [Object Properties dialog](#11-object-properties-dialog)
  12. [Paste](#12-paste)
- Widgets
  13. [Toolbox flyout and pin menu](#13-toolbox-flyout-and-pin-menu)
  14. [Canvas redraw queue](#14-canvas-redraw-queue)
  15. [Color picker and wheel sync flags](#15-color-picker-and-wheel-sync-flags)
  16. [Show Grid toggle](#16-show-grid-toggle)
  17. [Add-in repository refresh](#17-add-in-repository-refresh)
  18. [Dock panel items](#18-dock-panel-items)
  19. [Keyboard shortcut capture](#19-keyboard-shortcut-capture)
  20. [Preferences dialog](#20-preferences-dialog)
- [Issues](#issues)
- [Open items](#open-items)

## Hazards that span machines

Most of the bugs found came from one of these. Check every new state machine against them.

- **Nested main loops.** `RunBlocking` dialogs, `await` on `RunAsync`, and `ObjectOpacity.RefreshLayer` (which calls `MainContext.Iteration`) all dispatch input and timers before they return. While one runs, a gesture release, a shortcut, a tab switch, or a second request for the same flow can arrive.
- **Non-modal dialogs.** Effect dialogs, Layer Properties and Object Properties leave the main window live. Any state captured when the dialog opens can go stale: the active document, the current layer, or an object instance.
- **Reads of the active document after an await.** Read `workspace.ActiveDocument` after an await and you may get a different document. Capture the target `Document` before the await, and check `OpenDocuments.Contains` after it.
- **Persistent instances.** Effect instances live inside `EffectModifierNode` for the node's lifetime, and shape engines live in a static `SEngines` collection. Any state derived from their inputs has to be keyed on those inputs.

---

## 1. ToolManager pointer gesture

**Owner:** `Pinta.Core/Managers/ToolManager.cs`. This machine is explicit: `PointerGesture pointer_gesture`, plus `drag_document`, `drag_last_args` and `pointer_dispatch_depth`.

**States**
- `None`: no button is held.
- `ToolDrag`: `CurrentTool` took the press and is owed its moves and its release.
- `Pan`: a middle-button pan, routed to the pan tool.
- `Swallowed`: a press that no tool took, or whose drag was ended early. The rest of that press is dropped.

**Invariants**
- `ToolDrag` implies `drag_document` and `drag_last_args` are non-null, and the drag belongs to `CurrentTool`.
- Every Down a tool receives is followed by exactly one Up to that same tool, either real or synthetic (`EndToolDrag`).
- No tool receives an Up or a button-held Move for a press it did not receive.
- `pointer_dispatch_depth > 0` exactly while a tool's pointer handler is on the stack (`Dispatch`).

**Transitions**

| State | Event | Next / action |
|---|---|---|
| None, Swallowed | Down while a live preview is enabled | Swallowed |
| None, Swallowed | Down; a rasterize prompt (`ConfirmRasterizeToPaint`, `PrepareForSelectionRasterOp`) is cancelled or the bake fails | Swallowed |
| None, Swallowed | Down; the release arrives inside a prompt's nested loop | That Up sets None; the Down then sees `!= Swallowed` and is dropped |
| None, Swallowed | Down (middle) with a pan tool | Pan (`BeginPan`) |
| None, Swallowed | Down | ToolDrag; the down goes to `CurrentTool` |
| Pan | Down | ignored |
| ToolDrag | Down re-entered from inside a handler (shape tools forwarding a press) | Swallowed, then ToolDrag for the new tool |
| ToolDrag | Move with button held | ToolDrag; `drag_last_args` updated; forwarded unless a live preview is enabled |
| Swallowed | Move with button held | dropped |
| any non-Pan | hover Move | forwarded to `CurrentTool` |
| Pan | Move | forwarded to the pan tool unless a preview is enabled |
| ToolDrag | Up | None; Up goes to `CurrentTool`. Not gated by live preview. |
| Pan | Up (middle) | None; the pan tool gets the Up; the cursor becomes `CurrentTool.CurrentCursor` |
| Pan | Up (other button) | stays Pan |
| None, Swallowed | Up | None |
| ToolDrag | `SetCurrentTool`, `RemoveInstanceOfTool` or `Commit()` from outside dispatch (depth 0) | Swallowed; a synthetic Up with `drag_last_args` goes to the old tool (`EndToolDrag`) |
| ToolDrag | `SetCurrentTool` / `Commit()` from inside a handler (depth > 0) | ToolDrag; for a switch, the new tool owns the drag |
| ToolDrag | live preview start | `LivePreviewManager.RunSession` calls `tools.Commit()` before setting `IsEnabled`, so: Swallowed with a synthetic Up |
| ToolDrag | Undo / Redo (`EditActions`) | ignored while `IsToolDragActive` |
| ToolDrag | document close | `CloseDocumentAction` commits first, so: Swallowed with a synthetic Up |

**Cells that were reachable and invalid**

| Finding | Sequence | Status |
|---|---|---|
| A tool switch mid-drag left the old tool's drag state live | drag, press a tool shortcut, release, switch back: hovering paints, picks color, scrolls or zooms | fixed: `ToolManagerDragGestureTest.CommitEndsTheDragWithOneReleaseAndSwallowsTheRestOfThePress` |
| The Up was dropped during a live preview, so the drag or pan got stuck | drag, press an adjustment shortcut, release | fixed (the Up is ungated, and the preview's `Commit` ends the drag first): same test |
| A release during a preview committed a stroke and disposed the preview's render source | brush stroke on a layer with nodes, adjustment shortcut, release | fixed: ends at Commit, plus `LivePreviewSessionTest.RestartAfterAHistoryStepRendersFromThePreviewsOwnSnapshot` |
| Undo mid-drag wrote pixels with no history entry: an item's Undo calls `SetCurrentTool`, the synthetic Up pushes, and the push is dropped by `IsPerformingUndoRedo` | gradient finalized, PaintBrush stroke, Ctrl+Z while held | fixed: `ToolManagerDragGestureTest.UndoMidDragLeavesHistoryAlone` |
| A commit from a tool's own handler ended its own drag | the shape tools commit while forwarding | fixed: `ToolManagerDragGestureTest.CommitFromInsideTheToolsOwnHandlerKeepsTheDrag` |
| A release inside a prompt's nested loop delivered Up before Down | press on a transformed layer, release while the prompt is up | fixed, untested (needs a nested GTK loop) |
| The pan restored the stale cursor of the tool active before the pan | middle-drag, press `E`, release | fixed, untested |
| `HistoryListView` calls `History.Undo/Redo` directly, bypassing the `IsToolDragActive` guard | needs the history list to take input while a canvas drag holds the pointer | open: [INFERENCE] the GTK pointer grab blocks this |

## 2. Per-tool drag state

These machines are implicit. Each tool keeps its own "button held" fields. With the central fix in §1, every tool sees Down → Moves → exactly one Up, even across a tool switch, commit or preview start. The per-tool guards below cover an Up with no matching Down and state that has to outlive a commit.

| Tool (owner) | State fields | Invariant | Guards now in place | Status |
|---|---|---|---|---|
| Paint brush (`PaintBrushTool`, `BaseBrushTool`) | `mouse_button`, `undo_surface`, `surface_modified`, the splatter repeat timer | `ToolLayer` holds only this stroke; the timer runs only while held | `OnMouseUp` returns when `mouse_button == None`; clears `ToolLayer` after the commit; `OnDeactivated` calls `CancelRepeatingDraw` | fixed: `PaintBrushStrokeCommitTest.AReleaseWithoutAStrokeLeavesTheLayerAlone` |
| Pencil (`PencilTool`) | `mouse_button`, `undo_surface` | hover never draws | the central synthetic Up | fixed by §1 |
| Eraser (`EraserTool`) | inherited brush fields | as for the brush | the central synthetic Up | fixed by §1 |
| Recolor (`RecolorTool`) | inherited brush fields; rasterize prompt at stroke start | as for the brush | the central synthetic Up | fixed by §1 |
| Clone stamp (`CloneStampTool`) | `painting`, `offset`, `last_point`, `origin` | the stamp lands only after a press that set `painting` | `OnMouseUp`/`OnMouseMove` return unless `painting`; `OnDeactivated` resets `painting` | fixed by §1 plus the guard, untested |
| Gradient (`GradientTool`, `LineHandle`) | `handle.IsDragging`, `undo_surface` | finalized means not dragging | `Finalize`/`OnDeactivated` call `handle.EndDrag()` | fixed: `GradientToolSurfaceOwnershipTest.FinalizingMidDragEndsTheDrag` |
| Lasso (`LassoSelectTool`) | `is_dragging`, `hist`, `lasso_polygon`, mode | Up without a drag leaves the selection unchanged; a mode change never inherits a pending polygon | `OnMouseUp` returns unless `is_dragging`; a mode change calls `FinalizeShape` | orphan Up fixed: `LassoOrphanReleaseTest.AReleaseWithoutADragKeepsTheSelection`; mode change fixed, untested |
| Rectangle/ellipse select (`SelectTool`) | `handle` drag, `hist` | Up without a drag is ignored | already guarded; `OnDeactivated` rolls back | OK |
| Move selected / move selection (`MoveSelectedTool`, `BaseTransformTool`) | `is_dragging`, `is_rotating`, `is_scaling`, `using_mouse`, `hist`, `warp`, `center_snap_held` | a commit lands the gesture before anything it pushes | `OnCommit` calls `FinishActiveTransform`/`FinishWarp` first; a click lands a keyboard nudge whose release went astray; `OnDeactivated` clears `center_snap_held` | fixed, untested |
| Text (`TextTool`) | `is_editing`, `current_text_object`, `tracking`, `manipulation`, `drawing_new_box`, `history_document` | `tracking` implies `current_text_object != null`; history listeners are attached to exactly one document | `EndEditingSession` calls `EndManipulation`; the subscription follows `ActiveDocumentChanged` through `history_document` | fixed, untested |
| Text alignment (`TextPropertiesDialog`) | four toggle buttons | exactly one is active | radio group (`SetGroup`), `HandleAlignmentToggled` | fixed, untested |
| Color picker (`ColorPickerTool`) | `button_down` | hover never samples | the central synthetic Up; `SetCurrentTool` re-checks `CurrentTool == tool` after the Up, because the picker can switch tools on release | fixed by §1 |
| Pan (`PanTool`) | `active` | hover never scrolls | the central synthetic Up | fixed by §1 |
| Zoom (`ZoomTool`) | `mouse_down`, the overlay rectangle | the overlay is hidden when no drag is held | the central synthetic Up | fixed by §1 |

**Open:** in `BaseTransformTool`, `center_snap_held` and a keyboard nudge can still get stuck when the key release goes to another window while the tool stays active. No focus-out handler exists. Clicking recovers from the nudge.

## 3. Shape edit engine

**Owner:** `Pinta.Tools/Editable/EditEngines/BaseEditEngine.cs`. This machine is explicit for the gesture (`DragMode drag_mode`). The engine binding is implicit: the static `runtime_layer` and `SEngines`, plus the per-engine `SelectedShapeIndex` and `SelectedPointIndex`.

**States**
- `DragMode`: `None`, `MovingPoint`, `ChangingTension`, `MovingShape`, `RotatingShape`. `IsDrawing` means `!= None`.
- Binding: `runtime_layer` is null (unbound), or bound to a layer L whose `ShapeObjects` were loaded into `SEngines`.

**Invariants**
- `SEngines` holds exactly the engines for `runtime_layer`. Persisting (`PersistShapeObjects` → `ShapeEngineCollection.Store` → `UserLayer.ReplaceShapes`) only ever writes L's own engines back to L.
- Every finalize or persist targets the current layer of the active document. `EnsureShapesForCurrentLayer` rebinds before it, because switching documents fires no layer event.
- A drag never survives a change of `SEngines` or of the selection: `CancelGesture` resets `drag_mode` and `clicked_without_modifying`.
- `SelectedShapeIndex` and `SelectedPointIndex` are in range for `SEngines`, or -1.

**Transitions**

| State | Event | Next / action |
|---|---|---|
| None | press on a point, an edge or empty space | `MovingPoint`/`ChangingTension`/`MovingShape`/`RotatingShape`; a shape owned by another shape tool is first forwarded (`ForwardToCorrespondingTool`) on every branch, including Alt-rotate and right-drag |
| any drag | Up | None; one history item for the gesture |
| any drag | `HandleDeactivated` (tool switch) | None; rebind, `FinalizeAllShapes`, persist |
| any drag | `HandleCommit` (save, copy, layer op, live preview) | None; rebind, finalize, persist |
| any drag | `HandleBeforeUndo` / `HandleBeforeRedo` | None (the undo itself is also blocked mid-drag, §1) |
| any drag | engines rebuilt (`ReloadLayerShapes`, layer list change) | None (`ResetAfterEngineRebuild`); the selection is clamped |
| bound to L | document switch, then commit or save with no pointer move | rebinds to the new current layer before persisting |
| bound | `ResetShapes` | unbound (`runtime_layer = null`), so the next `Ensure` reloads |
| tool A, permanent switch to B (`ActivateCorrespondingTool`) | — | both engines `CancelGesture`; B takes A's selection indices |

**Cells that were reachable and invalid**

| Finding | Status |
|---|---|
| A stale `runtime_layer` after a tab switch: Ctrl+S persisted an empty engine list over doc2's shapes (data loss, no history) | fixed: `ShapeEditEngineStaleStateTest.CommittingAfterADocumentSwitchKeepsTheNewDocumentsShapes` |
| Undo mid-drag resumed the gesture against another shape | fixed: `ShapeEditEngineStaleStateTest.UndoMidDragEndsTheGestureInsteadOfEditingAnotherShape` |
| A commit mid-drag kept `drag_mode` | fixed: `ShapeEditEngineDragStateTest.CommittingMidDrawEndsTheGesture` |
| A deactivate mid-drag kept the drag | fixed: `ShapeEditEngineDragStateTest.DeactivatingMidDrawReleasesIsDrawingAndAcceptsAFreshMouseDownAfterwards` |
| Alt-rotate or right-drag on another tool's shape didn't forward, killed the gesture and baked Raster shapes | fixed, untested |
| `ResetShapes` cleared `SEngines` without unbinding, so the next persist wrote an empty list | fixed, untested (covered indirectly by the stale-state fixture) |
| `FinalizeAllShapes` leaves another tool's engine with a stale `SelectedShapeIndex` | open: not reproduced; the next permanent switch resets it |
| `DrawAllShapes(switchTools: false)` overwrites another engine's indices without try/finally | open: needs an exception mid-draw |

## 4. Document history and undo/redo

**Owner:** `Pinta.Core/Classes/DocumentHistory.cs`; guards in `Pinta.Core/Actions/EditActions.cs` (the undo and redo handlers). The fields are `Pointer`, `clean_pointer`, `ChangeCount`, `IsPerformingUndoRedo`, and `Document.IsDirty`.

**Invariants**
- `-1 <= Pointer < Count`, and `clean_pointer` is -1 or names a step that is still in `history`.
- `IsDirty == (Pointer != clean_pointer)` after every push, undo, redo, `SetClean`, `SetDirty` or `Clear`.
- `ChangeCount` strictly increases on every one of those operations and never repeats. Autosave fingerprints on it.
- No push is recorded while `IsPerformingUndoRedo`. That flag is set inside try/finally.
- No undo or redo starts while another is running, or while a tool drag is active.

**Transitions**

| Event | Effect |
|---|---|
| `PushNewItem` | truncates the redo items; `Pointer = Count-1`; `ChangeCount++`; if `clean_pointer >= Pointer`, then `clean_pointer = -1` (the saved step was truncated); dirty; `RefreshModifierComposites` |
| `PushNewItem` while `IsPerformingUndoRedo` | dropped |
| `Undo` | item.Undo inside the flag; `Pointer--`; `ChangeCount++`; clean iff `Pointer == clean_pointer` |
| `Redo` | `Pointer++`; `ChangeCount++`; item.Redo inside the flag; clean iff equal |
| `SetClean` / `SetDirty` / `Clear` | move the clean point; `ChangeCount++` |
| Undo/Redo action while `IsPerformingUndoRedo` (re-entered from `ObjectOpacity.RefreshLayer`'s `Iteration`) | ignored |
| Undo/Redo action while `tools.IsToolDragActive` | ignored |

| Finding | Status |
|---|---|
| Redo onto a new step at the index of a truncated saved step reported clean, so closing lost work without a prompt | fixed: `HistoryStateTest.RedoOntoTheIndexOfATruncatedSavedStepStaysDirty` |
| A double Ctrl+Z inside a nested `Iteration` undid one item twice | fixed, untested (needs a main loop that re-dispatches) |
| `ObjectOpacity.RefreshLayer` still pumps the main loop: a queued tool click pushes during undo (and is dropped), and the autosave idle can run mid-swap | open: removing `Iteration` changes busy-cursor painting; the guards above cover the corrupting case |
| `ChromeManager.MainWindowBusy` is a bool, not a depth count, so nested busy sections clear it early | open: cosmetic (cursor only) |

## 5. Autosave

**Owner:** `Pinta.Core/Managers/AutosaveManager.cs`. This machine is implicit: `timer_id`, `retry_timer_id`, `autosave_scheduled`, `deferred_since`, `armed_interval_seconds`, and per-document `document_slots`, `autosaved_states` (fingerprint) and `next_autosave_times` (backoff).

**Invariants**
- At most one interval timer, one queued idle attempt, and one pointer-held retry exist at a time.
- A document is never left unexported for more than `MAX_INTERVAL_SECONDS` because of pointer-held deferral (`MustForceThroughDeferral`).
- `autosaved_states[doc] == doc.History.ChangeCount` means the file on disk matches the document. The fingerprint never repeats for different contents.
- A clean document has no autosave file. A closed document has no slot.
- A failed export never blocks the documents after it, and is retried after `IntervalSeconds`, not on every tick.

**Transitions**

| State | Event | Next / action |
|---|---|---|
| stopped | `Start` | armed with the current `IntervalSeconds` |
| armed | tick, enabled | `ScheduleAutosave` (idle at `PRIORITY_LOW`, deduplicated by `autosave_scheduled`) |
| armed | tick with a changed interval | re-armed (the old timer returns false) |
| idle attempt | pointer held, deferral below the cap | one retry after 2 s; `deferred_since` kept |
| idle attempt | pointer held, deferral at the cap | export anyway |
| idle attempt | not held | `deferred_since = null`; `AutosaveDirtyDocuments` |
| per document | closed since the last pass | `Forget` (file and slot removed) |
| per document | fingerprint unchanged | skip |
| per document | clean | `Forget`; record the fingerprint |
| per document | backoff not yet due | skip |
| per document | export throws | backoff for `IntervalSeconds`; fingerprint not recorded |
| per document | export succeeds | record the fingerprint; next due time ∝ export duration (`NextIntervalSeconds`) |
| any | `Stop` | both timers removed, maps cleared, session directory deleted |

| Finding | Status |
|---|---|
| The fingerprint `(Pointer, Count)` repeated after undo then a new step, so the stale file survived a crash | fixed: `AutosaveResilienceTest.UndoThenANewStepIsAutosaved` |
| A clean (just saved) document kept its stale autosave | fixed: `AutosaveResilienceTest.SavingRemovesTheStaleAutosave` |
| A failure starved later documents; retry on every tick | fixed earlier: `AutosaveResilienceTest.AFailedExportDoesNotStarveTheDocumentsAfterIt`, `AFailedExportIsBackedOffRatherThanRetriedEveryTick` |
| Deferral forever during one long stroke | fixed earlier: `AutosaveResilienceTest.PointerHeldDefersAutosaveUntilItReachesTheMaxIntervalThenForcesItThrough` |
| A pending idle survives `Stop` | open: harmless, `App.Quit` follows immediately |

## 6. Active document

**Owner:** `Pinta.Core/Managers/WorkspaceManager.cs`. This machine is implicit: `active_document_index` over `open_documents`.

**Invariants**
- `HasOpenDocuments ⇔ active_document_index >= 0`, and the index always names an open document.
- `PreActiveDocumentChanged` fires before the index changes (tools commit against the old document). `ActiveDocumentChanged` fires after.
- `DocumentClosed` fires after `Document.Close()` has freed the layers. Handlers must not read the closed document's surfaces.

| Event | Effect |
|---|---|
| `ActivateDocument(new)` | appended; `SetActiveDocument(last)` |
| `SetActiveDocument(i)` to the same index | no-op (no events) |
| `SetActiveDocument(i)` | Pre → index = i → Changed |
| `CloseDocument(active)` | Pre → removed → index = max(0, i-1) or -1 → Changed → `Close` → `DocumentClosed` |
| `CloseDocument(other)` | removed; index shifted down if the removed one was below it; `Close` → `DocumentClosed` |

`ActiveDocument` and `ActiveWorkspace` throw with no documents open. Code that runs from timers, idles or continuations must use `ActiveDocumentOrDefault` (examples: the `MainWindow` refit idle, the `LivePreviewManager` poll).

## 7. Live preview session

**Owner:** `Pinta.Core/Managers/LivePreviewManager.cs` (`Run`, `RunSession` and its local functions). This machine is implicit: `is_running`, `IsEnabled`, `abandoned`, `renderAlive`, `renderHandle`, `pendingRestart`, `handlersInQueue`, plus the captured `doc`, `layer`, `selection` and `renderSource`.

**States:** Idle → Configuring (the non-modal dialog is open; renders restart on every setting change) → Applying (progress dialog) → Idle. Hidden is a sub-state of Configuring: the dialog is open but another tab is active, so `IsEnabled` is false. Abandoned is terminal: the previewed document was closed.

**Invariants**
- `is_running` covers the whole session, so a second effect is ignored rather than throwing.
- `IsEnabled ⇔ running ∧ ¬abandoned ∧ active document == doc`. The canvas only paints the preview surface over `doc`.
- The apply targets the captured `doc` and `layer`, never the active document.
- `renderSource` is an owned copy of `layer.Composite ?? layer.Surface`. It is disposed only after every render reading it has finished: `pendingRestart` and `renderHandle` are awaited in `finally`.
- The apply reads a render that reflects the final settings (`WaitForSettledRender`).
- No poll touches the workspace after the session is abandoned or finished.

**Transitions**

| State | Event | Next / action |
|---|---|---|
| Idle | effect menu (`Start`) | `tools.Commit()` (ends any drag, §1) → Configuring |
| Configuring or Applying | effect menu | ignored (`is_running`) |
| Configuring | setting changed | `RestartRender`: cancel, await, restart from `renderSource`; only the last queued handler restarts |
| Configuring | history push or undo on `doc` (the layer's composite is replaced) | unaffected: renders read the owned copy |
| Configuring | another tab activated | Hidden (`IsEnabled = false`); the dialog keeps editing `doc` |
| Hidden | `doc`'s tab activated again | Configuring |
| Configuring or Hidden | `doc` closed | Abandoned: render cancelled, polling stops, setting changes ignored |
| Configuring | Cancel | wait for the cancelled render → Idle |
| Configuring | OK | Applying: wait for the settled render |
| Abandoned | OK | returns; nothing applied |
| Applying | progress Cancel | `WasCanceled` → Idle |
| Applying | `layer` no longer in `doc` | nothing applied, no history |
| Applying | done | `AddModifierNode(FromEffect)` on `layer`; `doc.History.PushNewItem` |
| any | finally | `IsEnabled = false`, unsubscribe, hide progress, await renders, dispose `renderSource`, `is_running = false` |

| Finding | Status |
|---|---|
| A second effect while one was open threw an unhandled exception | fixed, untested |
| The history went to the active document instead of `doc`; tab-switch abandonment made OK silently do nothing | fixed: `LivePreviewSessionTest.OkAfterSwitchingTabsAppliesToThePreviewedDocument` |
| The render source was disposed by a history push while the dialog was open | fixed: `LivePreviewSessionTest.RestartAfterAHistoryStepRendersFromThePreviewsOwnSnapshot` |
| The poll read `workspace.Scale` after the last document closed | fixed: `LivePreviewSessionTest.ClosingThePreviewedDocumentStopsPolling` |
| OK applied to a deleted layer and truncated redo | fixed: `LivePreviewSessionTest.OkAfterThePreviewedLayerWasDeletedRecordsNothing` |
| A restart dropped existing nodes from the preview (wrong source) | fixed (single `renderSource`), untested |
| A race between OK and a settings change dropped the effect | fixed (`WaitForSettledRender`), untested (timing) |
| `is_running` was set before `try`, so an early throw disabled effects for the session | fixed (try/finally in `Run`), untested |
| The node captured the preview's `PropertyChanged` closure | fixed: `EffectDataCloneTest.ClonedEffectDataDoesNotNotifyTheOriginalsSubscribers` |

## 8. Effect nodes

**Owners:** `Pinta.Core/Classes/Re-editable/EffectModifierNode.cs`, `Pinta.Core/Effects/BaseEffect.cs` (`Clone`, `EffectData.Clone`), `Pinta.Core/Classes/ObservableObject.cs` (`ClearPropertyChangedSubscribers`), `Pinta.Gui.Widgets/Widgets/Layers/LayersListViewItemWidget.cs` (`ReconfigureModifier`), and the effects under `Pinta.Effects/Adjustments`.

**State:** the node's `cached_output`, keyed by `cached_input_fingerprint` and `cached_data_version`. `data_version` is bumped by `WatchEffectData` on `PropertyChanged`, or when the `EffectData` reference changes. The effect instance itself lives as long as the node.

**Invariants**
- A node's render depends only on its input pixels and its current `EffectData`. Nothing derived from earlier data or input survives on the instance unless it is keyed on that data. Examples: `BrightnessContrastEffect` and `CurvesEffect` use a `PixelOpCache` record keyed by settings; `PosterizeEffect` builds its op per render; `AutoLevelEffect` computes the histogram per render and is non-tileable.
- A cloned `EffectData` has no subscribers, so a node's data never notifies the live preview that created it.
- `CurvesData.Clone` deep-copies its control points, so a preview clone never shares lists with the dialog.
- Reconfigure Cancel restores the data the node had before (a new reference, so `data_version` bumps). OK pushes one `LayerObjectsHistoryItem`.
- Effect dialogs open showing the node's current data, not defaults.

| Event | Effect |
|---|---|
| `FromEffect(menuEffect)` | `CopyOf`: a fresh instance plus cloned data (no subscribers) |
| `Apply(input)`, fingerprint and version equal | cached copy |
| `Apply`, otherwise | render, replace the cache |
| data property changed / data replaced | `data_version++` |
| Reconfigure: dialog OK | refresh layer; push history |
| Reconfigure: dialog Cancel | `EffectData = dataBefore`; refresh layer; no history |

| Finding | Status |
|---|---|
| A Brightness/Contrast node rendered identity (its op was stale from a subscription on the original data) | fixed: `ModifierNodeReconfigureTest.BrightnessContrastNodeRendersItsCurrentSettings` |
| Posterize and Curves ops were cached per instance | fixed: `ModifierNodeReconfigureTest.PosterizeNodeRendersItsCurrentSettings`, `CurvesNodeRendersItsCurrentCurve` |
| Auto Level was frozen from the first input | fixed: `ModifierNodeReconfigureTest.AutoLevelNodeLevelsTheInputItIsGiven` |
| Curves clones shared control-point lists across threads | fixed: `ModifierNodeReconfigureTest.CurvesDataCopyDoesNotShareTheCurveBeingEdited` |
| Reconfigure Cancel left default settings and no undo (Curves, Levels, Align, Posterize dialogs reset on open) | fixed, untested (dialogs need GTK) |
| Curves right-click point removal didn't refresh the preview | fixed, untested |
| ORA load: a layer skipped before `CreateLayer` shifts the index-keyed sidecars (masks, effects, shapes, text) onto the wrong layers | open: needs a corrupt file |

## 9. Close, quit and save

**Owners:** `Impasto/Actions/File/CloseDocumentAction.cs` (`CloseDocument`, `PromptAndClose`, field `closing`), `Pinta.Core/Classes/InFlightGate.cs`, `Impasto/Actions/File/ExitAction.cs` (`is_exiting`), `Impasto/Actions/Window/CloseAllDocumentsAction.cs`, `Impasto/Actions/File/SaveDocumentImplementationAction.cs` (`SaveFileAs`, `ConfirmFlatten`, `IsOpen`), and `Impasto/MainWindow.cs` (`DockNotebook_TabClosed`, `HandleCloseRequest`).

**Per-document close**

| State | Event | Next / action |
|---|---|---|
| open | `CloseDocument(doc)` | registered in the gate (`InFlightGate.Run`); activate `doc`; `tools.Commit()` |
| closing | `CloseDocument(doc)` again (Quit, window close, Close All, tab close during a nested loop) | joins the in-flight task; no second prompt |
| closing, clean | — | closed → true |
| closing, dirty | — | prompting (`RunBlocking`, nested loop) |
| prompting | Cancel | false |
| prompting | Discard | closed if still open → true |
| prompting | Save | saving (`document.Save`) |
| saving | chooser cancelled or save failed | false |
| saving | saved | closed if still open → true |
| closing | flow ends | key released before the task completes, so a continuation may ask again |

**Save flow:** the Save As chooser is modal (`fcd.Modal = true`). After every await (chooser, `ConfirmFlatten`, `ConfirmBakeEffectNodes`), the flow stops when `!IsOpen(document)`. Flatten activates `document` before flattening, and never flattens another document.

**Quit / Close All**

| State | Event | Next |
|---|---|---|
| running | Exit or window close-request | exiting; `await CloseDocument(active)` for each document until none is left |
| exiting | Exit again | ignored |
| exiting | a close returns false | running |
| exiting | exception | running (`finally`), rethrown |
| exiting | no documents | quit |

**Invariants:** at most one close flow per document. Every step after an await acts on the captured document, and only while it is still open. Quit waits for each close to finish rather than inferring the outcome from document counts.

| Finding | Status |
|---|---|
| Exit inferred a cancel from an unchanged count while an `async void` close was still saving | fixed, untested |
| The non-modal Save As let a tab switch flatten or close the wrong document | fixed, untested |
| Re-entrant close of one document: a second prompt, then an export of a closed document | fixed: `CloseDocumentGateTest.ARequestWhileTheCloseIsInFlightJoinsItInsteadOfPromptingAgain`, `ARequestFromInsideTheFlowsBlockingPromptJoinsIt`, `OnceTheCloseFinishesANewRequestRunsAgain`, `ClosingOneDocumentDoesNotHoldBackAnother` |
| `JpegFormat`/`AvifFormat`/`WebPFormat` read `ActiveDocument` for the quality prompt | fixed, untested |
| `MainWindow` refit idle ran on whichever document was active | fixed, untested |
| `CompoundHistoryItem.StartSnapshotOfImage` snapshots `ActiveDocument`, not the resized document | open: no caller resizes a non-active document |

## 10. Layer Properties dialog

**Owners:** `Impasto/Actions/Layers/LayerPropertiesAction.cs` (`active_dialog`, `CancelDialogOnDocumentClosed`), `Impasto/Dialogs/LayerPropertiesDialog.cs`, and `Pinta.Gui.Widgets/Widgets/Layers/LayerPropertiesSession.cs`. The dialog is non-modal.

**State:** a `LayerPropertiesSession` captures `Document`, `Layer` and `InitialProperties` when the dialog opens. Live edits go through `Preview`, which writes to the captured layer, and to the selection layer only while the captured layer is current.

**Invariants:** every edit, Cancel and OK target the captured layer. Switching the current layer or the active document never retargets the dialog. The selection layer ends up drawn with the current layer's properties. OK records history at the captured layer's index, and only while the layer is still attached.

| State | Event | Next / action |
|---|---|---|
| closed | open | session captured; `active_dialog` set |
| open | shortcut again | submits OK |
| open | property edited | `Preview` |
| open | current layer or active document changes | unchanged target |
| open | captured document closed | Cancel |
| open | Cancel | `End(commit: false)`: initial properties restored; selection layer resynced |
| open | OK, layer attached | history pushed on `Document` |
| open | OK, layer detached | initial values restored; no history |

| Finding | Status |
|---|---|
| Cancel after clicking another layer copied the first layer's properties onto it | fixed: `LayerPropertiesSessionTest.PreviewAfterSwitchingLayersStillEditsTheOpenedLayer` |
| Closing the document with the dialog open threw | fixed, untested |
| Cancel after the current layer changed left the selection layer hidden | fixed: `LayerPropertiesSessionTest.CancelAfterSwitchingLayersDoesNotLeaveTheSelectionLayerHidden`, `CommitAfterSwitchingLayersDrawsTheSelectionLayerForTheCurrentLayer` |
| Undo in the main window while the dialog has live edits; OK then records stale initial values | open: needs a pre-record hook in history |

## 11. Object Properties dialog

**Owners:** `Pinta.Gui.Widgets/Widgets/Layers/ObjectPropertiesDialog.cs` and `ObjectPropertiesSession.cs`, plus `LayersListViewItem` (`NewPinnedToObject`). The dialog is non-modal.

**State:** the session holds the row pinned to the object instance and the document, its `Initial*` values, and `ended`. Edits apply live with no history.

**Invariants**
- Edits reach only the pinned object instance; reordering rows does not retarget them.
- When the object leaves its layer (deleted, baked, or replaced by `UserLayer.ReplaceShapes` on any shape persist, undo or redo), or its document closes, the session ends and raises `ObjectLost`, and the window closes. The dialog never stays open with controls that do nothing.
- Commit records one history item per changed property, and nothing for a lost object. Cancel restores the initial values.

| State | Event | Next |
|---|---|---|
| open | property edited, object available | applied live |
| open | edit, history change, or document closed, with the object gone | ended, `ObjectLost`, window closed |
| open | OK | `Commit` → ended |
| open | Cancel | `Cancel` → ended |

| Finding | Status |
|---|---|
| Index-based lookup edited the wrong object after a reorder | fixed: `PinnedObjectRowTest.ReorderingUnderThePinnedRowKeepsEditingItsOwnObject` |
| The window went inert after a shape persist replaced its object; Cancel could no longer revert | fixed: `ObjectPropertiesSessionTest.ReplacingTheObjectEndsTheSessionOnTheNextHistoryStep`, `ALiveChangeAfterASilentReplaceEndsTheSessionWithoutTouchingTheReplacement`, `CommitAfterTheObjectIsGoneRecordsNothing`, `CancelPutsTheStartingValuesBackOnTheObject`; `PinnedObjectRowTest.DeletedObjectMakesThePinnedRowInert` |
| Another history step recorded while the window holds uncommitted values snapshots those values, so undoing that step brings them back without a step of their own | open (`ponytail:` in `ObjectPropertiesSession`): needs a pre-record hook |

## 12. Paste

**Owner:** `Impasto/Actions/Edit/PasteAction.cs`. The target `doc` is captured before the async clipboard read.

**Invariant:** the paste goes ahead only while `doc` is still the active document (`IsActive`, checked after every await), because the commit, the Move Selection switch and the canvas scale all act on the active document. The size check and the resize use `doc`.

| Finding | Status |
|---|---|
| The size check and resize used the active document after the await | fixed, untested |
| Commit and tool switch acted on the active document after the read; a `doc` closed during the read was still pasted into | fixed (abandons when `doc` is no longer active), untested |

## 13. Toolbox flyout and pin menu

**Owner:** `Pinta.Gui.Widgets/Widgets/ToolBoxWidget.cs`: `open_pin_menu`, per-stack `OpenFlyout`, `HoverTimeoutId` and `CloseTimeoutId`.

**Invariants:** `open_pin_menu` is non-null only while that popover is shown; while it is set, flyouts stay closed and no scheduled close fires. Each stack has at most one pending hover timer and one pending close timer. A closed popover is unparented.

| State | Event | Next |
|---|---|---|
| idle | hover or long-press on a stacked button | hover timer → flyout open |
| flyout open | pointer leaves | close timer → idle |
| any | right-click | pin menu open (`open_pin_menu` set) |
| pin menu open | closed (dismissed, Pin or Unpin) | `OnClosed`: `open_pin_menu = null`, unparent |
| any | last tool of a stack removed | stack timers cancelled |

| Finding | Status |
|---|---|
| `open_pin_menu` was never cleared, so flyouts never opened again | fixed, untested |
| `activated_on_press` in `GtkExtensions.Widget` is not reset when the release lands off the button | open: not reachable from the keyboard (`CanFocus = false`) |

## 14. Canvas redraw queue

**Owner:** `Pinta.Gui.Widgets/Widgets/Canvas/PintaCanvas.cs` (`queued_update_id`).

**Invariant:** at most one idle redraw is queued. The id is reset before `UpdateCanvas` runs, so an exception cannot block later redraws. `Dispose` removes the pending idle.

Status: fixed, untested.

## 15. Color picker and wheel sync flags

**Owners:** `Pinta.Gui.Widgets/Widgets/ColorPickerPanel.cs` (`updating`, `applying_typed_code`, `color_picker_active`, `dragging_surface`) and `ColorWheelWidget.cs` (`updating`, `slider_release`).

**Invariants:** every re-entrancy flag is set and cleared in try/finally, so a throwing palette subscriber cannot leave the widget deaf to palette changes. The wheel commits the recent color on slider release through a capture-phase `EventControllerLegacy`, which sees releases the `Gtk.Range` drag gesture claims.

Status: fixed, untested.

## 16. Show Grid toggle

**Owner:** `Impasto/Actions/View/ShowGridToggledAction.cs`. Two stores: `CanvasGridManager.ShowGrid` and `view.ShowGrid.Value`.

**Invariant:** `view.ShowGrid.Value == canvas_grid.ShowGrid`. The toggle writes the grid; `SyncToggleFromGrid` on `canvas_grid.SettingsChanged` writes the toggle (the Edit Canvas Grid dialog and settings import). The setter skips equal values, so there is no feedback loop.

Status: fixed, untested.

## 17. Add-in repository refresh

**Owner:** `Pinta.Gui.Addins/AddinManagerDialog.cs` (`repository_refresh_running`).

**Invariant:** at most one repository refresh runs at a time; the flag is reset in `finally`. A `LoadAll` during a refresh reloads only the installed list, and the running refresh updates the repository lists when it finishes.

Status: fixed, untested.

## 18. Dock panel items

**Owner:** `Pinta.Docking/DockPanel.cs`. This machine is explicit: `DockPanelItem.State` is `ItemState { Docked, Minimized, Floating }`, plus `floated_from_minimized`.

| State | Event | Next |
|---|---|---|
| Docked | minimize | Minimized (`UpdateOnMinimize`) |
| Minimized | maximize | Docked (`UpdateOnMaximize`) |
| Docked or Minimized | float | Floating; remembers `floated_from_minimized`; the title bar is hidden (`SetFloating`) |
| Floating | float window closed | Minimized if it floated from minimized, else Docked |

**Invariants:** the item has one parent (pane, popover or float window). `State` agrees with `DockItem`'s button stack because the title bar, and with it Minimize and Maximize, is hidden while floating.

**Open:** `SaveSettings` stores `IsMinimized`, which is false while floating. An item floated from its icon and still floating at quit comes back docked, and the float state itself is never persisted. Not fixed: the intended restore behaviour is undecided.

## 19. Keyboard shortcut capture

**Owner:** `Impasto/Actions/Help/KeyboardShortcutsDialogAction.cs`: `pending_capture`, and a per-row `listening` flag.

**Invariants:** at most one row is listening. Every path that clears `pending_capture` (Esc, a tab switch, search, Reset All, OK, Cancel, reopening) also refreshes that row, which resets `listening`. Apply runs inside `Shortcuts.BeginBatch`/`EndBatch` in try/finally.

**Open:** Space and Enter may activate the capture button before the key controller sees them, so they cannot be bound. Unverified: needs a GTK run to confirm controller ordering.

## 20. Preferences dialog

**Owner:** `Impasto/Dialogs/PreferencesDialog.cs`.

**Invariants:** `canvas_surround_color_is_default` and `canvas_surround_color_button.DisplayColor` change together on every path (init, reset, pick, `RefreshFromSettings`). The settings export chooser is modal, so it cannot outlive the dialog.

| Finding | Status |
|---|---|
| Import of a default surround color left the old custom color showing, and OK then applied the default | fixed, untested |
| A non-modal export chooser could report errors on a destroyed dialog | fixed, untested |
| Import writes straight to the settings store; Cancel does not undo it | open: the message tells the user a restart applies it; staging changes the import UX |

---

## Issues

Confirmed bugs, grouped by root cause. P1 = silent data loss or a crash in a common flow; P2 = wrong target or a stuck UI in a reachable flow; P3 = cosmetic, or needs an unusual sequence.

| Issue | Priority | Member findings | Status | Regression tests |
|---|---|---|---|---|
| **Pointer gesture ownership.** A drag must end with exactly one release to the tool that took its press. | P1 | §1 tool switch, commit or preview mid-drag; Up dropped during preview; undo mid-drag; prompt nested loop; pan cursor. §2 brush, pencil, eraser, recolor and clone hover painting; brush orphan Up stamping `ToolLayer`; gradient handle drag outliving finalize; lasso orphan Up emptying the selection; color picker, pan and zoom hover; text tool NRE on commit mid-manipulation; MoveSelected commit order | fixed | `ToolManagerDragGestureTest` (3), `PaintBrushStrokeCommitTest.AReleaseWithoutAStrokeLeavesTheLayerAlone`, `GradientToolSurfaceOwnershipTest.FinalizingMidDragEndsTheDrag`, `LassoOrphanReleaseTest.AReleaseWithoutADragKeepsTheSelection` |
| **Shape engine binding.** `runtime_layer`/`SEngines` describe the current layer, and a gesture never outlives an engine rebuild. | P1 | §3 stale binding wiping shapes; undo mid-drag; commit or deactivate mid-drag; missing forward on rotate or right-drag; `ResetShapes` binding | fixed | `ShapeEditEngineStaleStateTest` (2), `ShapeEditEngineDragStateTest` (2) |
| **History clean point and autosave fingerprint.** `IsDirty` and the autosave file always reflect the document. | P1 | §4 truncated clean point; undo re-entry. §5 repeating fingerprint; stale file for a clean document | fixed | `HistoryStateTest.RedoOntoTheIndexOfATruncatedSavedStepStaysDirty`, `AutosaveResilienceTest.UndoThenANewStepIsAutosaved`, `AutosaveResilienceTest.SavingRemovesTheStaleAutosave` |
| **Live preview session lifetime.** The preview applies to what it previewed and never reads freed memory. | P1 | §7 history on the wrong document; tab-switch abandonment; freed render source; poll after close; deleted layer; second effect throwing; restart source; OK/restart race; `is_running` leak | fixed | `LivePreviewSessionTest` (4) |
| **Effect node derived caches.** A persistent effect instance renders only from its current data. | P2 | §8 Brightness/Contrast identity; Posterize, Curves and Auto Level stale ops; shared Curves lists; cloned subscribers; Reconfigure Cancel; dialogs resetting data on open | fixed | `ModifierNodeReconfigureTest` (5), `EffectDataCloneTest.ClonedEffectDataDoesNotNotifyTheOriginalsSubscribers` |
| **Close/quit/save prompt re-entrancy.** One close flow per document; nothing acts on a closed or wrong document after an await. | P2 | §9 Exit count inference; non-modal Save As retargeting flatten and close; re-entrant close of one document; export format prompts reading `ActiveDocument` | fixed | `CloseDocumentGateTest` (4) |
| **Dialogs bound to stale targets.** Non-modal dialogs and async actions act on what they opened for. | P2 | §10 Layer Properties wrong layer, document close, selection layer. §11 Object Properties reorder and inert-after-persist. §12 paste after a tab switch or close. §9 refit idle | fixed | `LayerPropertiesSessionTest` (3), `ObjectPropertiesSessionTest` (4), `PinnedObjectRowTest` (2) |
| **Widget sync flags and timers.** Re-entrancy flags and pending sources always return to rest. | P3 | §13 pin menu; §14 redraw id; §15 picker and wheel flags and release; §16 Show Grid; §17 add-in refresh; §20 surround color, export modal; text alignment radio; `FontFamilyDropDown` stale family | fixed | none (GTK widgets) |

## Open items

Scout findings that are not fixed, with the reason.

| Item | Where | Reason |
|---|---|---|
| Undo or redo from the History panel during a canvas drag bypasses the drag guard | `HistoryListView` | [INFERENCE] the pointer grab makes it unreachable; not reproduced |
| Key release lost to another window: `center_snap_held` and nudge, and other tools' key-held state; `MainWindow` forwards releases only while the canvas has focus | `BaseTransformTool`, `MainWindow` key-release handler | needs a focus-out reset design; a click recovers |
| `ObjectOpacity.RefreshLayer` pumps the main loop | `ObjectOpacity.RefreshLayerNoInvalidate` | the corrupting cases are guarded (§4); removing it changes busy feedback |
| `MainWindowBusy` is a bool | `ChromeManager` | cosmetic |
| ORA sidecar misattachment after a skipped layer | `OraFormat` import | needs a corrupt file |
| `CompoundHistoryItem` snapshots the active document | `CompoundHistoryItem.StartSnapshotOfImage` | no caller hits it |
| `FinalizeAllShapes` stale index on another engine; `DrawAllShapes` without try/finally | `BaseEditEngine` | not reproduced / exception-only |
| `activated_on_press` stuck | `GtkExtensions.Widget` | not reachable from the keyboard |
| `LayersListView.last_object_row` stored by position | `LayersListView` | undo paths observed re-selecting a row; not reproduced |
| Dock float state not persisted | `DockPanel.SaveSettings` | intended behaviour undecided |
| Space/Enter can't be captured as shortcuts | `KeyboardShortcutsDialogAction` | controller ordering unverified |
| Preferences import is not staged until OK | `PreferencesDialog` | UX decision |
| Undo under a live Layer Properties dialog; another step snapshotting Object Properties' uncommitted values | §10, §11 | needs a pre-record hook in history |
