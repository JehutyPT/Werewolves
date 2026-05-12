# todo: 
 
## remove the commented throw in HookSubPhaseStage
do this after all listener classes are implemented

## village fool and devoted servant interaction
the village fool should get night identification so we can prevent the devoted servant from triggering its power if the village fool gets lynched for the first time. 

## preset lists for different player counts

## role list validation
ensure there's at least 2 different teams in the game selected (ignoring ambiguous roles)

## create game creation config object
- group roles by team/kind 
- define preset lists for different player counts

## calculate odds for each team
min/max turn range until they will win, and average turn count assuming random decision?
or probability for different turn counts assuming random player decisions.

Also, game progression (how close is each team to victory in terms of % (i.e. 3 out of 4 werewolves left, so villagers are at 25% win progress))

## ability to revert state
is this possible without keeping a separate log of transient cache states? maybe if we just took a snapshot of current cache along with every log. So we don't need to know about all intermediate transitions, just what the cache is supposed to look like if we revert the game state to that log.

We also should not allow for edits to game log. Just revert to a specific log. and once that happens, just replay the entire history log to get to the current valid state.

Can also only ever remove logs from the top. Can't delete in the middle.