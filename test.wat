(module
  (type $i32_=>_i32 (;0;) (func (param i32) (result i32)))
  (type $i32_i32_=>_i32 (;1;) (func (param i32) (param i32) (result i32)))
  (import "env" "malloc" (;0;) (func $malloc (param i32) (result i32)))
  (memory (;0;) (export "memory") 1)
  (global $exit_code (;0;) (mut i32) i32.const 0)
  (global $fun_add*ptr (;1;) (mut i32) i32.const 4)
  (global $fun_makeCounters*ptr (;2;) (mut i32) i32.const 0)
  (global $fun_sub*ptr (;3;) (mut i32) i32.const 8)
  (global $heap_base (;4;) i32 i32.const 12)
  (global $var_c1 (;5;) (mut i32) i32.const 0)
  (global $var_c2 (;6;) (mut i32) i32.const 0)
  (table $func_table (;0;) 3 funcref)
  (elem (i32.const 0) (;0;) $fun_makeCounters)
  (elem (i32.const 1) (;1;) $fun_add)
  (elem (i32.const 2) (;2;) $fun_sub)
  (func $_start (;0;)  (result i32) 
    ;; execution start here:
    ;; Start of let
    ;; start of application
    ;; Load expression to be applied as a function
    global.get $fun_makeCounters*ptr ;; get global var: fun_makeCounters*ptr
    i32.load offset=4 ;; load closure environment pointer
    global.get $fun_makeCounters*ptr ;; get global var: fun_makeCounters*ptr
    i32.load ;; load table index
    call_indirect (type $i32_=>_i32) ;; call function
    ;; end of application
    global.set $var_c1 ;; set local var, have been hoisted
    ;; start of application
    ;; Load expression to be applied as a function
    ;; Start of field select
    global.get $var_c1 ;; get local var: var_c1, have been hoisted
    i32.load offset=0 ;; load field: f1
    ;; End of field select
    i32.load offset=4 ;; load closure environment pointer
    i32.const 2 ;; push 2 on stack
    ;; Start of field select
    global.get $var_c1 ;; get local var: var_c1, have been hoisted
    i32.load offset=0 ;; load field: f1
    ;; End of field select
    i32.load ;; load table index
    call_indirect (type $i32_i32_=>_i32) ;; call function
    ;; end of application
    i32.const 2 ;; push 2 on stack
    i32.eq
    i32.eqz ;; invert assertion
    (if 
      (then
        i32.const 42 ;; error exit code push to stack
        global.set $exit_code ;; set exit code
        unreachable ;; exit program
      )
    )
    ;; start of application
    ;; Load expression to be applied as a function
    ;; Start of field select
    global.get $var_c1 ;; get local var: var_c1, have been hoisted
    i32.load offset=0 ;; load field: f1
    ;; End of field select
    i32.load offset=4 ;; load closure environment pointer
    i32.const 5 ;; push 5 on stack
    ;; Start of field select
    global.get $var_c1 ;; get local var: var_c1, have been hoisted
    i32.load offset=0 ;; load field: f1
    ;; End of field select
    i32.load ;; load table index
    call_indirect (type $i32_i32_=>_i32) ;; call function
    ;; end of application
    i32.const 7 ;; push 7 on stack
    i32.eq
    i32.eqz ;; invert assertion
    (if 
      (then
        i32.const 42 ;; error exit code push to stack
        global.set $exit_code ;; set exit code
        unreachable ;; exit program
      )
    )
    ;; start of application
    ;; Load expression to be applied as a function
    ;; Start of field select
    global.get $var_c1 ;; get local var: var_c1, have been hoisted
    i32.load offset=4 ;; load field: f2
    ;; End of field select
    i32.load offset=4 ;; load closure environment pointer
    i32.const 1 ;; push 1 on stack
    ;; Start of field select
    global.get $var_c1 ;; get local var: var_c1, have been hoisted
    i32.load offset=4 ;; load field: f2
    ;; End of field select
    i32.load ;; load table index
    call_indirect (type $i32_i32_=>_i32) ;; call function
    ;; end of application
    i32.const 6 ;; push 6 on stack
    i32.eq
    i32.eqz ;; invert assertion
    (if 
      (then
        i32.const 42 ;; error exit code push to stack
        global.set $exit_code ;; set exit code
        unreachable ;; exit program
      )
    )
    ;; Start of let
    ;; start of application
    ;; Load expression to be applied as a function
    global.get $fun_makeCounters*ptr ;; get global var: fun_makeCounters*ptr
    i32.load offset=4 ;; load closure environment pointer
    global.get $fun_makeCounters*ptr ;; get global var: fun_makeCounters*ptr
    i32.load ;; load table index
    call_indirect (type $i32_=>_i32) ;; call function
    ;; end of application
    global.set $var_c2 ;; set local var, have been hoisted
    ;; start of application
    ;; Load expression to be applied as a function
    ;; Start of field select
    global.get $var_c2 ;; get local var: var_c2, have been hoisted
    i32.load offset=0 ;; load field: f1
    ;; End of field select
    i32.load offset=4 ;; load closure environment pointer
    i32.const 10 ;; push 10 on stack
    ;; Start of field select
    global.get $var_c2 ;; get local var: var_c2, have been hoisted
    i32.load offset=0 ;; load field: f1
    ;; End of field select
    i32.load ;; load table index
    call_indirect (type $i32_i32_=>_i32) ;; call function
    ;; end of application
    i32.const 10 ;; push 10 on stack
    i32.eq
    i32.eqz ;; invert assertion
    (if 
      (then
        i32.const 42 ;; error exit code push to stack
        global.set $exit_code ;; set exit code
        unreachable ;; exit program
      )
    )
    ;; start of application
    ;; Load expression to be applied as a function
    ;; Start of field select
    global.get $var_c2 ;; get local var: var_c2, have been hoisted
    i32.load offset=4 ;; load field: f2
    ;; End of field select
    i32.load offset=4 ;; load closure environment pointer
    i32.const 5 ;; push 5 on stack
    ;; Start of field select
    global.get $var_c2 ;; get local var: var_c2, have been hoisted
    i32.load offset=4 ;; load field: f2
    ;; End of field select
    i32.load ;; load table index
    call_indirect (type $i32_i32_=>_i32) ;; call function
    ;; end of application
    i32.const 5 ;; push 5 on stack
    i32.eq
    i32.eqz ;; invert assertion
    (if 
      (then
        i32.const 42 ;; error exit code push to stack
        global.set $exit_code ;; set exit code
        unreachable ;; exit program
      )
    )
    ;; End of let
    ;; End of let
    ;; if execution reaches here, the program is successful
    i32.const 0 ;; exit code 0
    return ;; return the exit code
  )
  (func $fun_add (;1;) (param $cenv i32) (param $arg_x i32) (result i32) 
    ;; local variables declarations:
    (local $Sptr i32)

    local.get 0 ;; get env pointer
    i32.load offset=0 ;; load value at offset: 0
    ;; Start of field select
    local.get 0 ;; get env pointer
    i32.load offset=0 ;; load value at offset: 0
    i32.load offset=0 ;; load field: value
    ;; End of field select
    local.get $arg_x ;; get local var: arg_x
    i32.add
    i32.store offset=0 ;; store int in struct
    local.get 0 ;; get env pointer
    i32.load offset=0 ;; load value at offset: 0
    i32.load offset=0 ;; load int from struct
  )
  (func $fun_makeCounters (;2;) (param $cenv i32) (result i32) 
    ;; local variables declarations:
    (local $Sptr i32)
    (local $Sptr$0 i32)
    (local $Sptr$1 i32)
    (local $Sptr$3 i32)
    (local $Sptr$4 i32)
    (local $Sptr$5 i32)
    (local $var_res i32)

    ;; Start of let
    ;; start of struct contructor
    i32.const 1 ;; size of struct
    i32.const 4 ;; 4 bytes
    i32.mul ;; multiply length with 4 to get size
    call $malloc ;; call malloc function
    local.set $Sptr ;; set struct pointer var
    local.get $Sptr ;; get struct pointer var
    i32.const 0 ;; push field offset to stack
    i32.add ;; add offset to base address
    ;; init field value
    i32.const 0 ;; push 0 on stack
    i32.store ;; store int field in memory
    local.get $Sptr ;; push struct address to stack
    ;; end of struct contructor
    local.set $var_res ;; set local var
    ;; start of struct contructor
    i32.const 2 ;; size of struct
    i32.const 4 ;; 4 bytes
    i32.mul ;; multiply length with 4 to get size
    call $malloc ;; call malloc function
    local.set $Sptr$1 ;; set struct pointer var
    local.get $Sptr$1 ;; get struct pointer var
    i32.const 0 ;; push field offset to stack
    i32.add ;; add offset to base address
    ;; init field f
    i32.const 1 ;; push 1 on stack
    i32.store ;; store int field in memory
    local.get $Sptr$1 ;; get struct pointer var
    i32.const 4 ;; push field offset to stack
    i32.add ;; add offset to base address
    ;; init field env
    i32.const 0 ;; push 0 on stack
    i32.store ;; store int field in memory
    local.get $Sptr$1 ;; push struct address to stack
    ;; end of struct contructor
    i32.const 4 ;; 4 byte offset
    i32.add ;; add offset
    ;; start of struct contructor
    i32.const 1 ;; size of struct
    i32.const 4 ;; 4 bytes
    i32.mul ;; multiply length with 4 to get size
    call $malloc ;; call malloc function
    local.set $Sptr$0 ;; set struct pointer var
    local.get $Sptr$0 ;; get struct pointer var
    i32.const 0 ;; push field offset to stack
    i32.add ;; add offset to base address
    ;; init field res
    local.get $var_res ;; get local var: var_res
    i32.store ;; store int field in memory
    local.get $Sptr$0 ;; push struct address to stack
    ;; end of struct contructor
    i32.store ;; store poninter in return struct
    local.get $Sptr$1 ;; get pointer to return struct
    global.set $fun_add*ptr
    ;; start of struct contructor
    i32.const 2 ;; size of struct
    i32.const 4 ;; 4 bytes
    i32.mul ;; multiply length with 4 to get size
    call $malloc ;; call malloc function
    local.set $Sptr$4 ;; set struct pointer var
    local.get $Sptr$4 ;; get struct pointer var
    i32.const 0 ;; push field offset to stack
    i32.add ;; add offset to base address
    ;; init field f
    i32.const 2 ;; push 2 on stack
    i32.store ;; store int field in memory
    local.get $Sptr$4 ;; get struct pointer var
    i32.const 4 ;; push field offset to stack
    i32.add ;; add offset to base address
    ;; init field env
    i32.const 0 ;; push 0 on stack
    i32.store ;; store int field in memory
    local.get $Sptr$4 ;; push struct address to stack
    ;; end of struct contructor
    i32.const 4 ;; 4 byte offset
    i32.add ;; add offset
    ;; start of struct contructor
    i32.const 1 ;; size of struct
    i32.const 4 ;; 4 bytes
    i32.mul ;; multiply length with 4 to get size
    call $malloc ;; call malloc function
    local.set $Sptr$3 ;; set struct pointer var
    local.get $Sptr$3 ;; get struct pointer var
    i32.const 0 ;; push field offset to stack
    i32.add ;; add offset to base address
    ;; init field res
    local.get $var_res ;; get local var: var_res
    i32.store ;; store int field in memory
    local.get $Sptr$3 ;; push struct address to stack
    ;; end of struct contructor
    i32.store ;; store poninter in return struct
    local.get $Sptr$4 ;; get pointer to return struct
    global.set $fun_sub*ptr
    ;; start of struct contructor
    i32.const 2 ;; size of struct
    i32.const 4 ;; 4 bytes
    i32.mul ;; multiply length with 4 to get size
    call $malloc ;; call malloc function
    local.set $Sptr$5 ;; set struct pointer var
    local.get $Sptr$5 ;; get struct pointer var
    i32.const 0 ;; push field offset to stack
    i32.add ;; add offset to base address
    ;; init field f1
    global.get $fun_add*ptr ;; get global var: fun_add*ptr
    i32.store ;; store int field in memory
    local.get $Sptr$5 ;; get struct pointer var
    i32.const 4 ;; push field offset to stack
    i32.add ;; add offset to base address
    ;; init field f2
    global.get $fun_sub*ptr ;; get global var: fun_sub*ptr
    i32.store ;; store int field in memory
    local.get $Sptr$5 ;; push struct address to stack
    ;; end of struct contructor
    ;; End of let
  )
  (func $fun_sub (;3;) (param $cenv i32) (param $arg_x$2 i32) (result i32) 
    ;; local variables declarations:
    (local $Sptr i32)

    local.get 0 ;; get env pointer
    i32.load offset=0 ;; load value at offset: 0
    ;; Start of field select
    local.get 0 ;; get env pointer
    i32.load offset=0 ;; load value at offset: 0
    i32.load offset=0 ;; load field: value
    ;; End of field select
    local.get $arg_x$2 ;; get local var: arg_x$2
    i32.sub
    i32.store offset=0 ;; store int in struct
    local.get 0 ;; get env pointer
    i32.load offset=0 ;; load value at offset: 0
    i32.load offset=0 ;; load int from struct
  )
  (data (i32.const 0) "\00")
  (data (i32.const 4) "\01")
  (data (i32.const 8) "\02")
  (export "_start" (func $_start))
  (export "exit_code" (global $exit_code))
  (export "heap_base_ptr" (global $heap_base))
)