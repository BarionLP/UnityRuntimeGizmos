# Unity Runtime Gizmos
forked from https://github.com/HiddenMonk/Unity3DRuntimeTransformGizmo  
*this fork is not production ready! use at your own risk*
- compatible with URP + RenderGraph API (Unity 6)
- add the `GizmosOutlineRendererFeature` and `TransformGizmosRendererFeature` to your renderer
- enable `Project Settings > Input System Package > Settings > Enable Input Consumption`
  - this makes actions with modifers block actions with the same key but without modifiers e.g. `Ctrl+Z` will not trigger `Z` 
  - use `Pass-Through` action type if you need to bypass this
- all game objects with a `RuntimeEditable` component can be edited
- optional delete functionality
