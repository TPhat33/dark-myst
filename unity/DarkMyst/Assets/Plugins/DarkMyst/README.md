# Generated assemblies

`DarkMyst.Combat.dll` and `DarkMyst.Content.dll` are built from `src/` by
`tools/build-unity-plugins.sh` and are not committed. Run that script before opening the
project, and again after any change under `src/`.

Do not add `Newtonsoft.Json.dll` here: Unity supplies it through the
`com.unity.nuget.newtonsoft-json` package, and a second copy breaks the build.
