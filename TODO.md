

# 1/30/2026
- [ ] Sort the new menu by name
- [ ] ctrl Snap bones from one to another when dragging in skeleton editor
- [ ] Drag the end of bones to size them in skeleton editor
- [ ] adding bones, renaming bones, and deleting bones causes animations to crash
- [ ] Create new animation with skeleton skeelcton should use that skeleton for animation
- [ ] Animation editor toggle for hiding bones
- [ ] Cancelling a rename seems to get you stuck so no input works
- [ ] Block input when rotating boes in animation
- [ ] cant see holds in the animation editor
- [ ] animation got corrupted by messing with holds
- [ ] Factored out the dopesheet and reused in sprite
- [ ] Added animated sprites
- [ ] Added stroke rendering
- [X] Stop Generating game assets on every load
- [X] Improved performance of rasterization in sprite editor
- [X] Switched to SixLabor.ImageSharp in editor
- [X] Removed shader compiler
- [X] Fixed bad performance in RectPacker
- [X] Added sprite size clamping to prevent sprites > atlas size
- [X] frame dot is too small
- [X] Show animation origin

# 1/29/2026
- [X] fixed native array bug copying array in constructor with 0 length
- [X] fix bone movement bug in skeleton editor
- [X] increased max animation frames to 64
- [X] Create new animation
- [X] Select skeleton for animation
- [X] Bone rename
- [X] Fixed display of bone names so they are the right color and position
- [X] Fixed bug where cancelling a bone extrude resulted in a broken skeleton
- [X] Skeleton document load now ensures bone length is at least a minimum value
- [X] Can now create a new skeleton document
- [X] new documents are now named "new_<type>" instead of just "new"
- [X] Update UI sizing and colors
- [X] Finished sprite constraints feature
- [X] Better snapping of documents during move with ctrl key
- [X] Can no longer enter edit mode while moving a document
- [X] Simplified hit testing in shapes and increased anchor hit size
- [X] remove forced pixel grid snapping in sprite editor
- [X] Refactored shape rasterization to support constrained sprites
- [X] Refactor and cleanup of Importer to simplify and fix race conditions
- [X] Fixed but where duplicating an asset would double it up in assets manifest
