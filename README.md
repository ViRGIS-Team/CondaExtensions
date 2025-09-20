# CondaExtensions
 
This UPM Package contains scripts that can be used by other UPM packages to integrate into the [Conda Package Management System](https://docs.conda.io/en/latest/).

# Documentation and Usage

The purpose and usage of this package are eplained in the following article :

[Package Documentation](https://medium.com/runic-software/using-conda-as-a-unity-package-version-2-c3442bf9c245)

## Version 2 Released

Version 2 of the Conda Extensions does away with the need to pre-install Conda. The package now downloads its own standalone copy of [micromamba](https://mamba.readthedocs.io/en/latest/installation/micromamba-installation.html), which provides all of the package management functions.

In Version 2 - the library location is moved to Assets/Conda/Env. This makes it more intuitive to delete the Conda folder if a refresh is needed.

Note that to upgrade from V1 to V2 - YOU MUST DELETE THE CONDA FOLDER AND RESTART UNITY.

Version 2 also removes the need for package installation scripts - this is now all done in C#.

# Use with Unity Cloud Build

As of Version 2 - the Conda Extensions will work in UNity Cloud Build out the box and no additional work, pre-scripts or post-scripts are needed.