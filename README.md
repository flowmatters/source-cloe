# Catchment constituent LOad Estimates (CLOE)

This repository acts as a reference implementation for the CLOE model of constituent estimation, and as a plugin for eWater Source. The code

## Reference implementation

CLOE has been conceptualised around the use of user-defined constituent sources, modelled within 'functional units' (or hydrological response units), in semi-lumped subcatchments, connected via a node-link network.

The C# code includes three model implementations of interest:

* `ArealCLOEModel`: Representing sources of constituents modelled as a function of area (eg of land use area)
* `NonArealCLOEModel`: Representing point sources of constituents
* `InstreamCLOEModel`: Representing processes modelled within the stream channel

In addition, the `AbstractCLOEFUModel` contains functionality common to the Areal and Non-Areal implementations.

## Use with eWater Source

### Compilation

You will need to compile against the version of Source you intend to use. Easiest option is to copy all the DLLs from Source into a `References` folder at within the repo.

### Use

CLOE is designed to represent multiple, user-defined constituent sources (eg paddock, hillslope, etc). You will need to first configure these constituent sources in Source and then apply CLOE to each one. After this, applying CLOE is much like applying any other constituent generation model and *can* be accomplished using the regular user interface in eWater Source.

This can be a tedious process. In our case studies, we used Python and Veneer to configure the Source model. See `cloe_setup.py` for inspiration.

## Reference

Fu et al, _A Framework for Modeling Constituent Loads in Data-Deficient Catchments: design and application of the CLOE hybrid conceptual-empirical framework_, Journal of Hydrology.

