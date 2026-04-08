INSTALL ADAPTIVEGI
----------------------------------

AdaptiveGI requires a few core Unity Registry packages to function. If Unity prompted you to install/upgrade package manager dependencies and you accepted, you can skip this setup. To install the packages manually:

1. Open the Package Manager (Window > Package Manager).
2. Change the package source in the top left of the package manager window to "Packages: Unity Registry"
3. Install the latest version for each of the following packages, some of which may already be installed depending on Unity version: "Mathematics", "Burst", "Collections", and "Shader Graph"

QUICK START
----------------------------------

Right click in the hierarchy and navigate to Adaptive GI > Adaptive GI to create the core GI manager. 
On this component you can find all the system's settings such as performance settings, visual settings, etc.
AdaptiveGI uses Unity's physics colliders for ray casting. This means any object you want to have contribute to GI needs to have a collider attached.
Objects without colliders can still receive GI, they just won't influence the environment.
You can use the GI Mask setting on the Adaptive GI manager to choose what physics layers you want to affect GI.

IMPORTANT: Make sure that Burst compilation is enabled in Jobs > Burst > Enable Compilation

From this point setup differs depending on what render pipeline your project uses:

Universal Render Pipeline:
	Depending on whether your URP project uses deferred rendering or not you need to do one of two things to get AdaptiveGI to render:

	1. Deferred/Deferred+ rendering
	If your project uses deferred/deferred+ rendering, all you have to do is go to your render pipeline asset's renderer and add the Adaptive GI Renderer Feature.
	IMPORTANT: The Adaptive GI Render Feature is only supported in Unity 6 and above. In older Unity versions, perform the Forward/Forward+ setup.

	2. Forward/Forward+ rendering
	If your project uses either Forward or Forward+ rendering, there is some manual shader/material conversion required.
	This is due to the lack of a GBuffer when using Forward rendering.
	For all the materials in your project using a base Universal Render Pipeline shader, you simply need to select all the materials you want to convert and navigate to Tools > Adaptive GI > Convert Selected Materials to Adaptive GI Compatible Shaders.
	For any materials using another custom shader, please refer to the documentation found at https://leogrieve.gitbook.io/adaptivegi/custom-shader-compatibility to add AdaptiveGI compatibility to custom shaders.

High Definition Render Pipeline:
	IMPORTANT: AdaptiveGI only supports setting the "Lit Shader Mode" in your HDRP Asset to "Deferred Only"
	After ensuring the Lit Shader Mode is set correctly, simply adding the Adaptive GI manager as described above to the scene is enough to get AdaptiveGI to render.

Refer to either the documentation found at https://leogrieve.gitbook.io/adaptivegi or the provided demo scenes for examples on how to use AdaptiveGI.
