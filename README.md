Ok, so this is gonna be a fun project I think. My goal is to actually make branches and document the making of this site.
I will try to do things following "Best Practices", or in the case of the first branch, it is gonna follow "Bost Practices".
- 1-create-base-project-structure
  - This is gonna follow some guidelines that Kevin Bost pointed me to in his github.com/keboo repo
  - First branch is just getting simple empty web application setup but with the central package management configured as well as a central place to manage the sdk/c# version to be used
  - There is also a global.json file that was added to make sure we stay on .net 8 since that is the LTS version at the moment (I may or may not upgrade to 9 when it is released, not sure yet)
- 2-add-aspire
  - This second step is going to add some of the initial steps for getting aspire working. This will be using version 9 rc 1 since that allows me to wait for the database to start up before proceeding with things
  - This will only add the basic extensions that are used as well as the app host, we will add migration application later once we start adding db elements
- 3-add-styling
  - This third step is a relatively small one since all we are going to do is add beercss as the styling library
  - We are going to try and only use the css elements for as long as possible, so even after pulling in the npm package, we will only copy over the css elements for now.