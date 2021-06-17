import json
import os
from glob import glob
import pandas as pd
import string

from pandas.core.algorithms import isin
from veneer import read_rescsv
from veneer.utils import _stringToList

MODEL_NAMES={
    'areal':'Source.CLOE.ArealCLOEModel',
    'non_areal':'Source.CLOE.NonArealCLOEModel'
}

def model_name(nm):
    return MODEL_NAMES.get(nm,nm)

def _read_csv(fn: str) -> pd.DataFrame:
    if fn.endswith('.res.csv'):
        _, data = read_rescsv(fn,['Name'])
        return data.reset_index()
    try:
        return pd.read_csv(fn,parse_dates=['Date'],dayfirst=True)
    except:
        return pd.read_csv(fn,parse_dates=True,dayfirst=True)

def read_csv(fn: str) -> pd.DataFrame:
    try:
        return _read_csv(fn)
    except:
        print(f'Error reading CSV data from {fn}')
        raise

def first_match(values,options):
    for o in options:
        if o in values:
            return o
    return None

def transform_df(df,k):
    if k in 'clover dairyshed feed septic fertiliser'.split(' '):
        catch_col = first_match(df.columns,['SourceName','cmt'])
        fu_col = first_match(df.columns,['Source_FU','FU'])
        df = df.copy()
        df['location'] = df[catch_col] + ':' + df[fu_col]
    return df

def custom_name(rec):
    sc = rec['NetworkElement']
    fu = rec['FunctionalUnit']
    rv = rec['RecordingVariable']
    src = rv.split('@')[3]
    return '%s@%s@@%s'%(sc,fu,src)

class CloeSetup(object):
    def __init__(self,fn,veneer):
        self.cfg = json.load(open(fn,'r'))

        self._find_sources()
       
        self._v = veneer
        self._query_network()

    def apply(self):
        self.load_inputs()
        self.create_constituents()
        self.create_constituent_sources()
        self.install_models()
        self.apply_parameters()

        self.create_data_sources()
        self.connect_time_series()
        self.setup_functions()

    def _find_sources(self):
        cfg = self.cfg['sources']
        self.areal_sources = cfg['areal']
        self.non_areal_sources = cfg['non_areal']
        self.conditional_sources = cfg.get('conditional',[])
        cond = list(set(sum([_stringToList(s['constrain'].get('sources',[])) for s in self.conditional_sources],[])))
        self.constituent_sources = self.areal_sources + self.non_areal_sources + cond

        self.constituents = list(cfg['constituents'].keys()) 

    def _query_network(self):
        network = self._v.network()
        network_df = network.as_dataframe()
        catchments = network_df[network_df['feature_type']=='catchment']
        self.catchment_names = list(catchments.name)
        self.fus = list(set(self._v.model.catchment.get_functional_unit_types()))

    def load_inputs(self):
        '''
        Find all CSV files in the working directory and load,
        identifying temporal and non-temporal input files.
        '''
        folder = self.cfg['inputs']['dir']
        files = glob(os.path.join(folder,'*.csv'))
        file_lookup = {fn.split('\\')[-1].split('.')[0]:fn for fn in files}
        if self.cfg['inputs']['ignore']:
            file_lookup = {k:fn for k,fn in file_lookup.items() if k not in self.cfg['inputs']['ignore']}
        inputs = {k:read_csv(fn) for k,fn in file_lookup.items()}
        self.inputs = {k:transform_df(df,k) for k,df in inputs.items()}
        self.temporal_inputs = [k for k,df in self.inputs.items() if 'Date' in df.columns]
        self.non_temporal_inputs = [k for k,df in self.inputs.items() if 'Date' not in df.columns]

    def create_constituents(self):
        for c in self.constituents:
            self._v.model.add_constituent(c)

    def create_constituent_sources(self):
        for src in self.constituent_sources:
            self._v.model.add_constituent_source(src)

    def install_models(self):
        self._v.model.catchment.generation.set_models(model_name('areal'),
                                                      sources=self.areal_sources,
                                                      constituents=self.constituents)
        self._v.model.catchment.generation.set_models(model_name('non_areal'),
                                                      sources=self.non_areal_sources,
                                                      constituents=self.constituents)

        for cond in self.conditional_sources:
            self._v.model.catchment.generation.set_models(model_name(cond['model']),**cond['constrain'])

        self._v.model.link.constituents.set_models('Source.CLOE.InstreamCLOEModel',constituents=self.constituents)
    
    def _constituent_specific_config(self,cfg_fn):
        cfg = self.cfg['inputs'].get('columns',{})
        for _,lookup in cfg.items():
            if cfg_fn in lookup:
                return True
        return False

    def create_data_sources(self):
        column_formats = self.cfg['inputs']['column_formats']
        self.data_source_lookup = {}

        # Load data sources
        for input_fn in self.temporal_inputs:
            print(input_fn,end=' ')
            df = self.inputs[input_fn]
            if not self._constituent_specific_config(input_fn):
                #(input_fn not in TP_INPUTS) and (input_fn not in TN_INPUTS):
                print("Don't need to pivot. Load as is")
                df = df.set_index('Date')
                self._v.create_data_source(input_fn,df,units='kg')
                self.data_source_lookup[input_fn] = (input_fn,column_formats.get(input_fn,None),df)
                continue

            print('Need to pivot')
            column_cfg = self.cfg['inputs']['columns']
            for constituent,lookup in column_cfg.items():
                #[('TP',TP_INPUTS),('TN',TN_INPUTS)]:
                if input_fn not in lookup:
                    continue
                pivot = df.pivot('Date','location',lookup[input_fn])
                data_source_name= '%s:%s'%(constituent,input_fn)
                self._v.create_data_source(data_source_name,pivot,units='kg')
                self.data_source_lookup[(input_fn,constituent)] = (data_source_name,'SC#${scix}:${fu}',pivot)
        # - do we need to scale by ha->m^2 -- NO. Model is in terms of per hectare...
    
    def map_model_data_sources(self):
        data_sources = self._v.data_sources()
        self.existing_data_sources = {ds['Name']:[i['Name'] for i in ds['Items'][0]['Details']] for ds in data_sources}

    def _apply_time_series(self,
                           data_source,
                           template,
                           constituent_source,
                           constituent,
                           columns,
                           fus=None,
                           param='InputRate'):
        if fus is not None:
            fus = _stringToList(fus)
        skipped = 0
        actioned = 0
        if columns is None:
            columns = self.existing_data_sources.get(data_source,None)
        if columns is None:
            print("WARNING. We don't know anything about the columns in {data_source}".format(data_source=data_source))
        skipped_columns = set()
        for catchment in self.catchment_names:
            for fu in (fus or self.fus):
                scix = catchment.split('#')[1]
                column = string.Template(template).substitute(fu=fu,sc=catchment,scix=scix,con=constituent)
                # msg = '%s:%s:%s:%s ==> %s/%s'%(catchment,fu,constituent_source,constituent,data_source,column)
                if column not in columns:
                    if column not in skipped_columns:
                        print('NO DATA: %s/%s'%(data_source,column))
                    skipped_columns = skipped_columns.union({column})
                    skipped += 1
                    continue
                try:
                    self._v.model.catchment.generation.assign_time_series(param,
                                                                    column,
                                                                    data_source,
                                                                    catchments=catchment,
                                                                    fus=fu,
                                                                    constituents=constituent,
                                                                    sources=constituent_source)
                    actioned += 1
                except:
                    print('Error assigning time series')
                    print('column=',column)
                    print('datasource=',data_source)
                    print('catchment=',catchment)
                    print('fus=',fu)
                    print('constituent=',constituent)
                    print('source=',constituent_source)
                    raise
        msg ='Applying timeseries from {datasource} to {constituent}/{source}. Applied {applied} and skipped {skipped}.'
        print(msg.format(datasource=data_source,constituent=constituent,source=constituent_source,applied=actioned,skipped=skipped))
    #         break

    def connect_time_series(self):
        self.map_model_data_sources()
        # constituent_cfg = self.cfg['inputs'].get('columns',{})
        exclusions = self.cfg['sources']['constituents']
        global_sources = self.cfg['inputs']['source']
        constrained_sources = self.cfg['inputs'].get('existing',None)
        if constrained_sources is None:
            constrained_sources = self.cfg['inputs'].get('constrained',None)

        self._v.model.catchment.generation.clear_time_series('InputRate')
        for con_src in self.constituent_sources:
        #     print(src)
            for con in self.constituents:
            # for con, custom_sources in exclusions.items():
                if con_src in exclusions.get(con,{}).get('exclude',[]):
                    print('SKIPPING %s:%s - exclude list'%(con_src,con))
                    continue

                if con_src in global_sources:
                    # Default treatment. Apply data source to all FUs/Constituents for which we have data
                    # CURRENTLY NOT USED FOR Tully
                    data_fn = self.cfg['inputs']['source'][con_src]
                    print('\n%s:%s'%(con_src,con),end='')
                    data_source, column_template, df = self.data_source_lookup.get(data_fn,self.data_source_lookup.get((data_fn,con),(None,None,None)))
                    if data_source is None:
                        data_source = data_fn
                    if column_template is None:
                        column_template = self.cfg['inputs']['column_formats'][data_fn]
                    print(' - %s --> %s'%(data_source,column_template))
                    self._apply_time_series(data_source,column_template,con_src,con,None if df is None else df.columns)
                else:
                    # Apply in specific circumstances
                    constrained_config = constrained_sources.get(con_src,[])
                    if not len(constrained_config):
                        print('SKIPPING %s:%s - no inputs'%(con_src,con))
                        continue

                    if isinstance(constrained_config,dict):
                        constrained_config = [constrained_config]

                    for config in constrained_config:
                        constrain = config.get('constrain',{})
                        self._apply_time_series(config['datasource'],
                                                config['column_format'],
                                                con_src,
                                                constrain.get('constituents',self.constituents),
                                                None,
                                                constrain.get('fus',None),
                                                param=config.get('param','InputRate'))
                        
        # for each source, constituent, (skip some combinations)
        #   if we have a temporal input rate, assign it...
        self._connect_global_time_series()

    def _connect_global_time_series(self):
        for ts in self.cfg['inputs'].get('global',[]):
            self._v.model.catchment.generation.assign_time_series(ts['parameter'],ts['column'],ts['source'],**ts['constrain'])

    def extract_spatial_parameters(self,sp):
        result = []
        data = self.inputs[sp['input']]
        for _,row in data.iterrows():
            matches = {k:row[v] for k,v in sp['match'].items()}
            matches.update(sp.get('constrain',{}))
            val = row[sp['value']]
            result.append((val,matches))
        return result

    def apply_parameters(self):
        cfg = self.cfg['parameters']

        target = self._v.model.catchment.generation
        print('Setting fixed scalar parameters')
        for p,val in cfg.get('fixed',{}).items():
            target.set_param_values(p,val)

        print('Configure loss parameters')
        losses = cfg['losses']
        for p,frac in losses.get('fixed',{}).items():
            target.set_param_values('O%s'%p,0.0)
            target.set_param_values('D%s'%p,frac)

        for p in losses.get('dynamic',[]):
            target.set_param_values('O%s'%p,1.0)
            target.set_param_values('D%s'%p,0.0)

        print('Setting constrained scalar parameters')
        for scalar in cfg.get('scalar',[]):
            matches = scalar.get('match',{})
            for param,val in scalar.get('parameters',{}).items():
                target.set_param_values(param,val,**matches)

        print('Setting spatial parameters')
        spatial = cfg.get('spatial',[])
        for sp in spatial:
            actions = self.extract_spatial_parameters(sp)
            for val,matches in actions:
                target.set_param_values(sp['param'],val,**matches)

    def setup_functions(self):
        cfg = self.cfg.get('runoff_functions',[])
        for fn in cfg:
            self._setup_function(fn)
    
    def _setup_function(self,fn):
        general_function = fn['template']
        constraint = fn.get('constrain',{})
        if 'fus' in constraint:
            runoff_constraint = {
                'fus':constraint['fus']
            }
        else:
            runoff_constraint = {}

        print(f'Configuring runoff function {fn["function_name"]} for {fn["param"]}')
        use_format = '{' in general_function

        function_parameters = fn.get('model_variables',[fn.get('runoff_variable',None)])
        if function_parameters[0] is None:
            function_parameters = []

        if use_format:
            variables = {}
        else:
            variables = []

        for fp in function_parameters:
            var_pattern = '$'+fp.replace(' ','_').replace('-','_')
            all_variables = self._v.variables()
            vars_to_delete = [v for v in all_variables._select(['FullName']) if v.startswith(var_pattern)]
            if len(vars_to_delete):
                print(f'Deleting existing variables with prefix: {var_pattern}')
                self._v.model.functions.delete_variables(vars_to_delete)

            self._v.model.catchment.runoff.create_modelled_variable(fp,**runoff_constraint)

            all_variables = self._v.variables()
            vars_for_fp = [v for v in all_variables._select(['FullName']) if v.startswith(var_pattern)]
            # print(fp,vars_for_fp[:5])

            if use_format:
                variables[var_pattern[1:]] = vars_for_fp
            else:
                variables.append(vars_for_fp)

            self._v.model.functions.set_modelled_variable_time_period('Current Time Step',vars_for_fp)

        function_names = [v.replace(var_pattern[1:],fn['function_name']) for v in vars_for_fp]

        for scalar in fn.get('model_parameters',[]):
            values = self._v.model.catchment.runoff.get_param_values(scalar,**runoff_constraint)
            # print(scalar,values)

            if use_format:
                variables[scalar] = values
            else:
                variables.append(values)

        for data_parameter in fn.get('data_variables',[]):
            assert use_format
            actions = self.extract_spatial_parameters(data_parameter)
            values = []
            for sc, fu in self._v.model.catchment.runoff.enumerate_names(**runoff_constraint):
                scix = int(sc.split('#')[-1])
                val_found = data_parameter.get('default_value',-1)
                for val,matches in actions:
                    if 'catchments' in matches:
                        if matches['catchments'] != sc:
                            continue
                    if 'fus' in matches:
                        if matches['fus'] != fu:
                            continue
                    val_found = val
                    break
                values.append(val_found)
            if len(values)!=len(function_names):
                print(f'Expected length of values ({len(values)}) to match number of functions ({len(function_names)})')
                assert len(values)==len(function_names)
            # Now, align with parameters... catchment, fu
            variables[data_parameter['label']] = values            
            # need the names...

        # print(function_names[:5])

        self._v.model.functions.delete_functions(function_names)

        if use_format:
            keys = list(variables.keys())
            function_arguments = [{k:variables[k][ix] for k in keys} for ix in range(len(variables[keys[0]]))]
        else:
            function_arguments = [vs for vs in zip(*variables)]
        res = self._v.model.functions.create_functions(function_names,
                                                       general_function,
                                                       function_arguments,
                                                       use_format=use_format)
        # print(res)

        if 'units' in fn:
            self._v.model.functions.set_options('ResultUnit','UnitLibrary.%s'%fn['units'],functions=function_names)
        
        models = self._v.model.catchment.generation.model_table(**constraint)
        element_names = [(row['Catchment'],row['Functional Unit']) for _,row in models.iterrows() if row['model'] and row['model'].endswith('CLOEModel')]
        #element_names = self._v.model.catchment.generation.enumerate_names(**constraint)
        fn_applications = [('$%s_%s_%s'%(fn['function_name'],n[0],n[1])).replace('#','').replace('- ','').replace(' ','_') for n in element_names]

        self._v.model.catchment.generation.clear_time_series(fn['param'],**constraint)
        self._v.model.catchment.generation.apply_function(fn['param'],fn_applications,**constraint)
        self._v.model.functions.set_time_of_evaluation('DuringFlowPhase',functions=fn_applications)

class CloeScenario(object):
    def __init__(self,v):
        self._v = v

    def record_stores(self):
        variables = [
            'SoilStore',
            'GroundwaterStore'
        ]
        recorders = [{'RecordingVariable':v} for v in variables]
        self._v.configure_recording(enable=recorders)

    def record_fluxes(self):
        variables = [
            'LossOut',
            'LossToGroundwater',
            'LossOutGroundwater'
        ]
        recorders = [{'RecordingVariable':v} for v in variables]
        self._v.configure_recording(enable=recorders)


    def retrieve_soil_stores(self,constituent='TP',run='latest',run_data=None):
        return self.retrieve_model_var_by_fu('SoilStore',constituent,run,run_data)

    def retrieve_gw_stores(self,constituent='TP',run='latest',run_data=None):
        return self.retrieve_model_var_by_fu('GroundwaterStore',constituent,run,run_data)

    def retrieve_gw_loss(self,constituent='TP',run='latest',run_data=None):
        return self.retrieve_model_var_by_fu('LossToGroundwater',constituent,run,run_data)

    def retrieve_loss_to_outside(self,constituent='TP',run='latest',run_data=None):
        return self.retrieve_model_var_by_fu('LossOut$',constituent,run,run_data)

    def retrieve_model_var_by_fu(self,variable,constituent='TP',run='latest',run_data=None):
        criteria = {
            'RecordingVariable':'Constituents@%s.*@Generation Model@%s'%(constituent,variable)
        }
        table = self._v.retrieve_multiple_time_series(run,run_data,criteria,name_fn=custom_name)

        return sum_dataframe(table,'@@',sum_element=1)
        # column_names = {c.split('@@')[0] for c in table.columns}
        # reduced = {cn:sum([table[c] for c in table.columns if c.startswith(cn)]) for cn in column_names}
        # return pd.DataFrame(reduced)

    def retrieve_quickflow_flux(self,constituent='TP',run='latest',run_data=None):
        return self.retrieve_fu_flux('Quick Flow Load Out',constituent,run,run_data)

    def retrieve_slowflow_flux(self,constituent='TP',run='latest',run_data=None):
        return self.retrieve_fu_flux('Slow Flow Load Out',constituent,run,run_data)

    def retrieve_fu_flux(self,variable,constituent='TP',run='latest',run_data=None):
        criteria = {
            'RecordingVariable':'Constituents@%s.*@%s'%(constituent,variable)
        }
        table = self._v.retrieve_multiple_time_series(run,run_data,criteria,name_fn=custom_name)

        return sum_dataframe(table,'@@',sum_element=1)

def sum_dataframe(df,column_delim='@',sum_element=2):
    def make_new_name(col):
        split = col.split(column_delim)
        keep = [s for i,s in enumerate(split) if i != sum_element]
        return column_delim.join(keep)

    column_names = {make_new_name(c) for c in df.columns}
    reduced = {cn:sum([df[c] for c in df.columns if make_new_name(c)==cn]) for cn in column_names}
    return pd.DataFrame(reduced)

def sum_for_catchment(df):
    return sum_dataframe(df,column_delim='@',sum_element=1)

