# CondaExtensions
 
This UPM Package contains scripts that can be used by other UPM packages to integrate into the [Conda Package Management System](https://docs.conda.io/en/latest/).

# Documentation and Usage

The purpose and usage of this package are explained in the following article :

[Package Documentation](https://medium.com/runic-software/using-conda-as-a-unity-package-version-2-c3442bf9c245)

## Version 3 Released

Version 3 of the Conda Extensions does away with Conda. The package now downloads its own standalone copy of [pixi](https://mamba.readthedocs.io/en/latest/installation/micromamba-installation.html), which provides all of the package management functions.

In Version 3, the library location is moved to Assets/Conda/Plugins/.... This brings it in line with Unity standards and makes it more intuitive to delete the Conda folder if a refresh is needed.

Note that to upgrade from V2 to V3 - YOU MUST DELETE THE CONDA FOLDER AND RESTART UNITY.


# Cross Compiling

As of version 3 - Conda extensions can be used to cross compile on similar platforms - so you can compile on 
OSX platforms between x86 and arm86 and similarly on Linux platforms.

You must use the `CONDA_ARCH_OVERRIDE` environment varaible, which can have the following values

`osx-64`
`osx-arm64`
`linux-64`
`linux-aarch64`
 `win-64`

# Use with Unity Cloud Build - Cross Compiling

As of Version 2, the Conda Extensions will work in Unity Cloud Build out of the box, requiring no additional work, pre-scripts, or post-scripts.

Uniy Cloud Build extensively utilises macOS on Apple Silicon as its runners. To build for osx-64, you need to have a separate build and override the Conda architecture by creating the following environment variable in the Unity Cloud Build configuration (or similar for different build hosts) :

Name | Value
--- | --- |
CONDA_ARCH_OVERRIDE | osx-64
