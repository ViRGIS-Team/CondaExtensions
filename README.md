# CondaExtensions
 
This UPM Package contains scripts that can be used by other UPM packages to integrate into the [Conda Package Management System](https://docs.conda.io/en/latest/).

# Documentation and Usage

The purpose and usage of this package are explained in the following article :

[Package Documentation](https://medium.com/runic-software/using-conda-as-a-unity-package-version-2-c3442bf9c245)

## Version 2 Released

Version 2 of the Conda Extensions does away with the need to pre-install Conda. The package now downloads its own standalone copy of [micromamba](https://mamba.readthedocs.io/en/latest/installation/micromamba-installation.html), which provides all of the package management functions.

In Version 2, the library location is moved to Assets/Conda/Env. This makes it more intuitive to delete the Conda folder if a refresh is needed.

Note that to upgrade from V1 to V2 - YOU MUST DELETE THE CONDA FOLDER AND RESTART UNITY.

Version 2 also removes the need for package installation scripts - this is now all done in C#.

## MacOs Architecture

In Version 2, the extension will install the correct platform architecture based on the architecture of the Unity Editor by default (see below for override). This is mostly only important for macOS builds. Unity by default builds Universal binaries but these extensions can only put one set of binaries into the build.

If you are building on Apple Silicon, you will get an app that works on 'osx-arm64'.

If you are building on Intel Silicon, you will get an app that works on 'osx-64'.

If this is not what you want, for instance, in Cloud Build ...

# Use with Unity Cloud Build - Cross Compiling

As of Version 2, the Conda Extensions will work in Unity Cloud Build out of the box, requiring no additional work, pre-scripts, or post-scripts.

Uniy Cloud Build extensively utilises macOS on Apple Silicon as its runners. To build for osx-64, you need to have a separate build and override the Conda architecture by creating the following environment variable in the Unity Cloud Build configuration (or similar for different build hosts) :

Name | Value
--- | --- |
CONDA_ARCH_OVERRIDE | osx-64
