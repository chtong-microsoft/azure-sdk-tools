﻿// ----------------------------------------------------------------------------------
//
// Copyright Microsoft Corporation
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// http://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
// ----------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Azure.Subscriptions;
using Microsoft.Azure.Subscriptions.Models;
using Microsoft.WindowsAzure.Commands.Common.Factories;
using Microsoft.WindowsAzure.Commands.Common.Interfaces;
using Microsoft.WindowsAzure.Commands.Common.Models;
using Microsoft.WindowsAzure.Commands.Common.Properties;
using Microsoft.WindowsAzure.Commands.Utilities.Common;
using Microsoft.WindowsAzure.Commands.Utilities.Common.Authentication;
using Microsoft.IdentityModel.Clients.ActiveDirectory;

namespace Microsoft.WindowsAzure.Commands.Common
{
    /// <summary>
    /// Convenience client for azure profile and subscriptions.
    /// </summary>
    public class ProfileClient
    {
        public static IDataStore DataStore { get; set; }

        public AzureProfile Profile { get; private set; }

        public Action<string> WarningLog;

        public Action<string> DebugLog;

        private void WriteDebugMessage(string message)
        {
            if (DebugLog != null)
            {
                DebugLog(message);
            }
        }

        private void WriteWarningMessage(string message)
        {
            if (WarningLog != null)
            {
                WarningLog(message);
            }
        }

        private static void UpgradeProfile()
        {
            string oldProfileFilePath = System.IO.Path.Combine(AzurePowerShell.ProfileDirectory, AzurePowerShell.OldProfileFile);
            string newProfileFilePath = System.IO.Path.Combine(AzurePowerShell.ProfileDirectory, AzurePowerShell.ProfileFile);
            if (DataStore.FileExists(oldProfileFilePath))
            {
                string oldProfilePath = System.IO.Path.Combine(AzurePowerShell.ProfileDirectory,
                    AzurePowerShell.OldProfileFile);
                AzureProfile oldProfile = new AzureProfile(DataStore, oldProfilePath);

                if (DataStore.FileExists(newProfileFilePath))
                {
                    // Merge profile files
                    AzureProfile newProfile = new AzureProfile(DataStore, newProfileFilePath);
                    foreach (var environment in newProfile.Environments.Values)
                    {
                        oldProfile.Environments[environment.Name] = environment;
                    }
                    foreach (var subscription in newProfile.Subscriptions.Values)
                    {
                        oldProfile.Subscriptions[subscription.Id] = subscription;
                    }
                    DataStore.DeleteFile(newProfileFilePath);
                }

                // Save the profile to the disk
                oldProfile.Save();
                
                // Rename WindowsAzureProfile.xml to WindowsAzureProfile.json
                DataStore.RenameFile(oldProfilePath, newProfileFilePath);
            }
        }

        static ProfileClient()
        {
            DataStore = new DiskDataStore();
        }

        public ProfileClient()
            : this(System.IO.Path.Combine(AzurePowerShell.ProfileDirectory, AzurePowerShell.ProfileFile))
        {

        }

        public ProfileClient(string profilePath)
        {
            ProfileClient.UpgradeProfile();

            Profile = new AzureProfile(DataStore, profilePath);

            WarningLog = (s) => Debug.WriteLine(s);
        }

        #region Account management

        public AzureAccount AddAccountAndLoadSubscriptions(AzureAccount account, AzureEnvironment environment, SecureString password)
        {
            if (environment == null)
            {
                throw new ArgumentNullException("environment");
            }

            if (account == null)
            {
                throw new ArgumentNullException("account");
            }

            var subscriptionsFromServer = ListSubscriptionsFromServer(ref account, environment, password, ShowDialog.Always).ToList();

            Debug.Assert(account != null);

            // If account id is null the login failed
            if (account.Id != null)
            {
                // Add the account to the profile
                AddOrSetAccount(account);

                // Update back Profile.Subscriptions
                foreach (var subscription in subscriptionsFromServer)
                {
                    AddOrSetSubscription(subscription);
                }

                if (Profile.DefaultSubscription == null)
                {
                    var firstSubscription = Profile.Subscriptions.Values.FirstOrDefault();
                    if (firstSubscription != null)
                    {
                        SetSubscriptionAsDefault(firstSubscription.Name, firstSubscription.Account);
                    }
                }

                return Profile.Accounts[account.Id];
            }
            else
            {
                return null;
            }
        }

        public AzureAccount AddOrSetAccount(AzureAccount account)
        {
            if (account == null)
            {
                throw new ArgumentNullException("Account needs to be specified.", "account");
            }

            if (Profile.Accounts.ContainsKey(account.Id))
            {
                Profile.Accounts[account.Id] =
                    MergeAccountProperties(account, Profile.Accounts[account.Id]);
            }
            else
            {
                Profile.Accounts[account.Id] = account;
            }

            // Update in-memory environment
            if (AzureSession.CurrentContext != null && AzureSession.CurrentContext.Account != null &&
                AzureSession.CurrentContext.Account.Id == account.Id)
            {
                AzureSession.SetCurrentContext(AzureSession.CurrentContext.Subscription,
                    AzureSession.CurrentContext.Environment,
                    Profile.Accounts[account.Id]);
            }

            return Profile.Accounts[account.Id];
        }

        public AzureAccount GetAccountOrDefault(string accountName)
        {
            if (string.IsNullOrEmpty(accountName))
            {
                return AzureSession.CurrentContext.Account;
            }
            else if (AzureSession.CurrentContext.Account != null && AzureSession.CurrentContext.Account.Id == accountName)
            {
                return AzureSession.CurrentContext.Account;
            }
            else if (Profile.Accounts.ContainsKey(accountName))
            {
                return Profile.Accounts[accountName];
            }
            else
            {
                throw new ArgumentException(string.Format("Account with name '{0}' does not exist.", accountName), "accountName");
            }
        }

        public AzureAccount GetAccountOrNull(string accountName)
        {
            if (string.IsNullOrEmpty(accountName))
            {
                throw new ArgumentNullException("accountName");
            }

            if (Profile.Accounts.ContainsKey(accountName))
            {
                return Profile.Accounts[accountName];
            }
            else
            {
                return null;
            }
        }

        public AzureAccount GetAccount(string accountName)
        {
            var account = GetAccountOrNull(accountName);
            
            if (account == null)
            {
                throw new ArgumentException(string.Format("Account with name '{0}' does not exist.", accountName), "accountName");
            }

            return account;
        }

        public IEnumerable<AzureAccount> ListAccounts(string accountName)
        {
            List<AzureAccount> accounts = new List<AzureAccount>();
            
            if (!string.IsNullOrEmpty(accountName))
            {
                if (Profile.Accounts.ContainsKey(accountName))
                {
                    accounts.Add(Profile.Accounts[accountName]);
                }
            }
            else
            {
                accounts = Profile.Accounts.Values.ToList();
            }

            return Profile.Accounts.Values;
        }

        public AzureAccount RemoveAccount(string accountId)
        {
            if (string.IsNullOrEmpty(accountId))
            {
                throw new ArgumentNullException("User name needs to be specified.", "userName");
            }

            if (!Profile.Accounts.ContainsKey(accountId))
            {
                throw new ArgumentException("User name is not valid.", "userName");
            }

            AzureAccount account = Profile.Accounts[accountId];
            Profile.Accounts.Remove(account.Id);

            foreach (AzureSubscription subscription in account.GetSubscriptions(Profile))
            {
                if (subscription.Account == accountId)
                {
                    AzureAccount defaultAccount = GetDefaultAccount(subscription.Id);

                    // There's no default account to use, remove the subscription.
                    if (defaultAccount == null)
                    {
                        // Warn the user if the removed subscription is the default one.
                        if (subscription.IsPropertySet(AzureSubscription.Property.Default))
                        {
                            WriteWarningMessage(Resources.RemoveDefaultSubscription);
                        }

                        // Warn the user if the removed subscription is the current one.
                        if (subscription.Equals(AzureSession.CurrentContext.Subscription))
                        {
                            WriteWarningMessage(Resources.RemoveCurrentSubscription);
                        }

                        Profile.Subscriptions.Remove(subscription.Id);
                    }
                }
            }

            return account;
        }

        private AzureAccount GetDefaultAccount(Guid subscriptionId)
        {
            List<AzureAccount> accounts = ListSubscriptionAccounts(subscriptionId);
            AzureAccount account = accounts.FirstOrDefault(a => a.Type != AzureAccount.AccountType.Certificate);

            if (account != null)
            {
                // Found a non-certificate account.
                return account;
            }

            // Use certificate account if its there.
            account = accounts.FirstOrDefault();

            return account;
        }

        #endregion

        #region Subscripton management

        public AzureSubscription AddOrSetSubscription(AzureSubscription subscription)
        {
            if (subscription == null)
            {
                throw new ArgumentNullException("Subscription needs to be specified.", "subscription");
            }
            if (subscription.Environment == null)
            {
                throw new ArgumentNullException("Environment needs to be specified.", "subscription.Environment");
            }
            // Validate environment
            GetEnvironmentOrDefault(subscription.Environment);

            if (Profile.Subscriptions.ContainsKey(subscription.Id))
            {
                Profile.Subscriptions[subscription.Id] = MergeSubscriptionProperties(subscription, Profile.Subscriptions[subscription.Id]);
            }
            else
            {
                Debug.Assert(!string.IsNullOrEmpty(subscription.Account));
                if (!Profile.Accounts.ContainsKey(subscription.Account))
                {
                    throw new KeyNotFoundException(string.Format("The specified account {0} does not exist in profile accounts", subscription.Account));
                }

                Profile.Subscriptions[subscription.Id] = subscription;
            }

            // Update in-memory subscription
            if (AzureSession.CurrentContext != null && AzureSession.CurrentContext.Subscription != null &&
                AzureSession.CurrentContext.Subscription.Id == subscription.Id)
            {
                var account = GetAccountOrDefault(subscription.Account);
                var environment = GetEnvironmentOrDefault(subscription.Environment);
                AzureSession.SetCurrentContext(Profile.Subscriptions[subscription.Id], environment, account);
            }

            return Profile.Subscriptions[subscription.Id];
        }

        public AzureSubscription RemoveSubscription(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentNullException("Subscription name needs to be specified.", "name");
            }

            var subscription = Profile.Subscriptions.Values.FirstOrDefault(s => s.Name == name);

            if (subscription == null)
            {
                throw new ArgumentException(string.Format(Resources.SubscriptionNameNotFoundMessage, name), "name");
            }
            else
            {
                return RemoveSubscription(subscription.Id);
            }
        }

        public AzureSubscription RemoveSubscription(Guid id)
        {
            if (!Profile.Subscriptions.ContainsKey(id))
            {
                throw new ArgumentException(Resources.SubscriptionIdNotFoundMessage, "name");
            }

            var subscription = Profile.Subscriptions[id];

            if (subscription.IsPropertySet(AzureSubscription.Property.Default))
            {
                WriteWarningMessage(Resources.RemoveDefaultSubscription);
            }

            // Warn the user if the removed subscription is the current one.
            if (AzureSession.CurrentContext.Subscription != null && subscription.Id == AzureSession.CurrentContext.Subscription.Id)
            {
                WriteWarningMessage(Resources.RemoveCurrentSubscription);
                AzureSession.SetCurrentContext(null, null, null);
            }

            Profile.Subscriptions.Remove(id);

            // Remove this subscription from its associated AzureAccounts
            List<AzureAccount> accounts = ListSubscriptionAccounts(id);

            foreach (AzureAccount account in accounts)
            {
                account.RemoveSubscription(id);
                if (!account.IsPropertySet(AzureAccount.Property.Subscriptions))
                {
                    Profile.Accounts.Remove(account.Id);
                }
            }

            return subscription;
        }

        public List<AzureSubscription> RefreshSubscriptions(AzureEnvironment environment)
        {
            if (environment == null)
            {
                throw new ArgumentNullException("environment");
            }

            var subscriptionsFromServer = ListSubscriptionsFromServerForAllAccounts(environment);

            // Update back Profile.Subscriptions
            foreach (var subscription in subscriptionsFromServer)
            {
                // Resetting back default account
                if (Profile.Subscriptions.ContainsKey(subscription.Id))
                {
                    subscription.Account = Profile.Subscriptions[subscription.Id].Account;
                }
                AddOrSetSubscription(subscription);
            }

            return Profile.Subscriptions.Values.ToList();
        }

        public AzureSubscription GetSubscription(Guid id)
        {
            if (Profile.Subscriptions.ContainsKey(id))
            {
                return Profile.Subscriptions[id];
            }
            else
            {
                throw new ArgumentException(Resources.SubscriptionIdNotFoundMessage, "id");
            }
        }

        public AzureSubscription GetSubscription(string name)
        {
            AzureSubscription subscription = Profile.Subscriptions.Values
                .FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (subscription != null)
            {
                return subscription;
            }
            else
            {
                throw new ArgumentException(Resources.SubscriptionIdNotFoundMessage, "name");
            }
        }

        public AzureSubscription SetSubscriptionAsCurrent(string name, string accountName)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentNullException("name", string.Format(Resources.InvalidSubscription, name));
            }

            AzureSubscription currentSubscription = null;
            var subscription = Profile.Subscriptions.Values.FirstOrDefault(s => s.Name == name);

            if (subscription == null)
            {
                throw new ArgumentException(string.Format(Resources.InvalidSubscription, name), "name");
            }
            else
            {
                currentSubscription = new AzureSubscription { Id = subscription.Id };
                currentSubscription = MergeSubscriptionProperties(subscription, currentSubscription);
                var environment = GetEnvironmentOrDefault(subscription.Environment);
                accountName = string.IsNullOrEmpty(accountName) ? subscription.Account : accountName;
                var account = GetAccount(accountName);
                currentSubscription.Account = account.Id;
                AzureSession.SetCurrentContext(currentSubscription, environment, account);
            }

            return currentSubscription;
        }

        public AzureSubscription SetSubscriptionAsDefault(string name, string accountName)
        {
            AzureSubscription subscription = SetSubscriptionAsCurrent(name, accountName);

            if (subscription != null)
            {
                Profile.DefaultSubscription = subscription;
            }

            return subscription;
        }

        public void ClearDefaultSubscription()
        {
            Profile.DefaultSubscription = null;
        }

        public void ImportCertificate(X509Certificate2 certificate)
        {
            DataStore.AddCertificate(certificate);
        }

        public List<AzureAccount> ListSubscriptionAccounts(Guid subscriptionId)
        {
            return Profile.Accounts.Where(a => a.Value.HasSubscription(subscriptionId))
                .Select(a => a.Value).ToList();
        }

        public List<AzureSubscription> ImportPublishSettings(string filePath, string environmentName)
        {
            var azureEnvironment = GetEnvironmentOrDefault(environmentName);
            var subscriptions = ListSubscriptionsFromPublishSettingsFile(filePath, azureEnvironment);
            if (subscriptions.Any())
            {
                var thumbprint = subscriptions.First().Account;
                Profile.Accounts[thumbprint] = new AzureAccount
                {
                    Id = thumbprint,
                    Type = AzureAccount.AccountType.Certificate
                };
                Profile.Accounts[thumbprint].SetSubscriptions(subscriptions);

                foreach (var subscription in subscriptions)
                {
                    subscription.Properties[AzureSubscription.Property.SupportedModes] =
                        AzureModule.AzureServiceManagement.ToString();

                    AddOrSetSubscription(subscription);
                }
            }
            return subscriptions;
        }

        private List<AzureSubscription> ListSubscriptionsFromPublishSettingsFile(string filePath, AzureEnvironment environment)
        {
            if (string.IsNullOrEmpty(filePath) || !DataStore.FileExists(filePath))
            {
                throw new ArgumentException("File path is not valid.", "filePath");
            }
            return PublishSettingsImporter.ImportAzureSubscription(DataStore.ReadFileAsStream(filePath), environment.Name).ToList();
        }

        private IEnumerable<AzureSubscription> ListSubscriptionsFromServerForAllAccounts(AzureEnvironment environment)
        {
            // Get all AD accounts and iterate
            var accountNames = Profile.Accounts.Keys;

            List<AzureSubscription> subscriptions = new List<AzureSubscription>();

            foreach (var accountName in accountNames.ToArray())
            {
                var account = Profile.Accounts[accountName];

                if (account.Type != AzureAccount.AccountType.Certificate)
                {
                    subscriptions.AddRange(ListSubscriptionsFromServer(ref account, environment, null, ShowDialog.Never));
                }

                AddOrSetAccount(account);
            }

            if (subscriptions.Any())
            {
                return subscriptions;
            }
            else
            {
                return new AzureSubscription[0];
            }
        }

        private IEnumerable<AzureSubscription> ListSubscriptionsFromServer(ref AzureAccount account, AzureEnvironment environment, SecureString password, ShowDialog promptBehavior)
        {
            try
            {
                if (!account.IsPropertySet(AzureAccount.Property.Tenants))
                {
                    LoadAccountTenants(ref account, environment, password, promptBehavior);
                }
            }
            catch (AadAuthenticationException aadEx)
            {
                WriteOrThrowAadExceptionMessage(aadEx);
                return new AzureSubscription[0];
            }

            try
            {

                List<AzureSubscription> mergedSubscriptions = MergeSubscriptions(
                    ListServiceManagementSubscriptions(ref account, environment, password, ShowDialog.Never).ToList(),
                    ListResourceManagerSubscriptions(ref account, environment, password, ShowDialog.Never).ToList());

                // Set user ID
                foreach (var subscription in mergedSubscriptions)
                {
                    subscription.Environment = environment.Name;
                    subscription.Account = account.Id;
                    account.SetOrAppendProperty(AzureAccount.Property.Subscriptions, subscription.Id.ToString());
                }

                if (mergedSubscriptions.Any())
                {
                    return mergedSubscriptions;
                }
                else
                {
                    return new AzureSubscription[0];
                }
            }
            catch (AadAuthenticationException aadEx)
            {
                WriteOrThrowAadExceptionMessage(aadEx);
                return new AzureSubscription[0];
            }
        }

        private void LoadAccountTenants(ref AzureAccount account, AzureEnvironment environment, SecureString password, ShowDialog promptBehavior)
        {
            var commonTenantToken = AzureSession.AuthenticationFactory.Authenticate(ref account, environment,
                AuthenticationFactory.CommonAdTenant, password, promptBehavior);
            using (var subscriptionClient = AzureSession.ClientFactory.CreateCustomClient<Azure.Subscriptions.SubscriptionClient>(
                new TokenCloudCredentials(commonTenantToken.AccessToken),
                environment.GetEndpointAsUri(AzureEnvironment.Endpoint.ResourceManager)))
            {
                account.SetOrAppendProperty(AzureAccount.Property.Tenants, 
                    subscriptionClient.Tenants.List().TenantIds.Select(ti => ti.TenantId).ToArray());
            }
        }

        private List<AzureSubscription> MergeSubscriptions(List<AzureSubscription> subscriptionsList1,
            List<AzureSubscription> subscriptionsList2)
        {
            if (subscriptionsList1 == null)
            {
                subscriptionsList1 = new List<AzureSubscription>();
            }
            if (subscriptionsList2 == null)
            {
                subscriptionsList2 = new List<AzureSubscription>();
            }

            Dictionary<Guid, AzureSubscription> mergedSubscriptions = new Dictionary<Guid, AzureSubscription>();
            foreach (var subscription in subscriptionsList1.Concat(subscriptionsList2))
            {
                if (mergedSubscriptions.ContainsKey(subscription.Id))
                {
                    mergedSubscriptions[subscription.Id] = MergeSubscriptionProperties(mergedSubscriptions[subscription.Id],
                        subscription);
                }
                else
                {
                    mergedSubscriptions[subscription.Id] = subscription;
                }
            }
            return mergedSubscriptions.Values.ToList();
        }

        private AzureSubscription MergeSubscriptionProperties(AzureSubscription subscription1, AzureSubscription subscription2)
        {
            if (subscription1 == null || subscription2 == null)
            {
                throw new ArgumentNullException("subscription1");
            }
            if (subscription1.Id != subscription2.Id)
            {
                throw new ArgumentException("Subscription Ids do not match.");
            }
            AzureSubscription mergedSubscription = new AzureSubscription
            {
                Id = subscription1.Id,
                Name = subscription1.Name,
                Environment = subscription1.Environment,
                Account = subscription1.Account ?? subscription2.Account
            };

            // Merge all properties
            foreach (AzureSubscription.Property property in Enum.GetValues(typeof(AzureSubscription.Property)))
            {
                string propertyValue = subscription1.GetProperty(property) ?? subscription2.GetProperty(property);
                if (propertyValue != null)
                {
                    mergedSubscription.Properties[property] = propertyValue;
                }
            }

            // Merge RegisteredResourceProviders
            var registeredProviders = subscription1.GetPropertyAsArray(AzureSubscription.Property.RegisteredResourceProviders)
                    .Union(subscription2.GetPropertyAsArray(AzureSubscription.Property.RegisteredResourceProviders), StringComparer.CurrentCultureIgnoreCase);

            mergedSubscription.SetProperty(AzureSubscription.Property.RegisteredResourceProviders, registeredProviders.ToArray());

            // Merge SupportedMode
            var supportedModes = subscription1.GetPropertyAsArray(AzureSubscription.Property.SupportedModes)
                    .Union(subscription2.GetPropertyAsArray(AzureSubscription.Property.SupportedModes), StringComparer.CurrentCultureIgnoreCase);

            mergedSubscription.SetProperty(AzureSubscription.Property.SupportedModes, supportedModes.ToArray());

            // Merge Tenants
            var tenants = subscription1.GetPropertyAsArray(AzureSubscription.Property.Tenants)
                    .Union(subscription2.GetPropertyAsArray(AzureSubscription.Property.Tenants), StringComparer.CurrentCultureIgnoreCase);

            mergedSubscription.SetProperty(AzureSubscription.Property.Tenants, tenants.ToArray());

            return mergedSubscription;
        }

        private AzureEnvironment MergeEnvironmentProperties(AzureEnvironment environment1, AzureEnvironment environment2)
        {
            if (environment1 == null || environment2 == null)
            {
                throw new ArgumentNullException("environment1");
            }
            if (environment1.Name != environment2.Name)
            {
                throw new ArgumentException("Subscription Ids do not match.");
            }
            AzureEnvironment mergedEnvironment = new AzureEnvironment
            {
                Name = environment1.Name
            };

            // Merge all properties
            foreach (AzureEnvironment.Endpoint property in Enum.GetValues(typeof(AzureEnvironment.Endpoint)))
            {
                string propertyValue = environment1.GetEndpoint(property) ?? environment2.GetEndpoint(property);
                if (propertyValue != null)
                {
                    mergedEnvironment.Endpoints[property] = propertyValue;
                }
            }

            return mergedEnvironment;
        }

        private AzureAccount MergeAccountProperties(AzureAccount account1, AzureAccount account2)
        {
            if (account1 == null || account2 == null)
            {
                throw new ArgumentNullException("account1");
            }
            if (account1.Id != account2.Id)
            {
                throw new ArgumentException("Account1 Ids do not match.");
            }
            if (account1.Type != account2.Type)
            {
                throw new ArgumentException("Account1 types do not match.");
            }
            AzureAccount mergeAccount = new AzureAccount
            {
                Id = account1.Id,
                Type = account1.Type
            };

            // Merge all properties
            foreach (AzureAccount.Property property in Enum.GetValues(typeof(AzureAccount.Property)))
            {
                string propertyValue = account1.GetProperty(property) ?? account2.GetProperty(property);
                if (propertyValue != null)
                {
                    mergeAccount.Properties[property] = propertyValue;
                }
            }

            // Merge Tenants
            var tenants = account1.GetPropertyAsArray(AzureAccount.Property.Tenants)
                    .Union(account2.GetPropertyAsArray(AzureAccount.Property.Tenants), StringComparer.CurrentCultureIgnoreCase);

            mergeAccount.SetProperty(AzureAccount.Property.Tenants, tenants.ToArray());

            // Merge Subscriptions
            var subscriptions = account1.GetPropertyAsArray(AzureAccount.Property.Subscriptions)
                    .Union(account2.GetPropertyAsArray(AzureAccount.Property.Subscriptions), StringComparer.CurrentCultureIgnoreCase);

            mergeAccount.SetProperty(AzureAccount.Property.Subscriptions, subscriptions.ToArray());

            return mergeAccount;
        }

        private IEnumerable<AzureSubscription> ListResourceManagerSubscriptions(ref AzureAccount account, AzureEnvironment environment, SecureString password, ShowDialog promptBehavior)
        {
            List<AzureSubscription> result = new List<AzureSubscription>();

            foreach (var tenant in account.GetPropertyAsArray(AzureAccount.Property.Tenants))
            {
                try
                {
                    var tenantToken = AzureSession.AuthenticationFactory.Authenticate(ref account, environment, tenant, password, ShowDialog.Never);
                    
                    using (var subscriptionClient = AzureSession.ClientFactory.CreateCustomClient<Azure.Subscriptions.SubscriptionClient>(
                                new TokenCloudCredentials(tenantToken.AccessToken),
                                environment.GetEndpointAsUri(AzureEnvironment.Endpoint.ResourceManager)))
                    {
                        var subscriptionListResult = subscriptionClient.Subscriptions.List();
                        foreach (var subscription in subscriptionListResult.Subscriptions)
                        {
                            AzureSubscription psSubscription = new AzureSubscription
                            {
                                Id = new Guid(subscription.SubscriptionId),
                                Name = subscription.DisplayName,
                                Environment = environment.Name
                            };
                            psSubscription.SetProperty(AzureSubscription.Property.SupportedModes, AzureModule.AzureResourceManager.ToString());
                            psSubscription.SetProperty(AzureSubscription.Property.Tenants, tenant);

                            AzureSession.SubscriptionTokenCache[Tuple.Create(psSubscription.Id, account.Id)] = tenantToken;

                            result.Add(psSubscription);
                        }
                    }
                }
                catch (CloudException cEx)
                {
                    WriteOrThrowAadExceptionMessage(cEx);
                }
                catch (AadAuthenticationException aadEx)
                {
                    WriteOrThrowAadExceptionMessage(aadEx);
                }
            }

            return result;
        }

        private IEnumerable<AzureSubscription> ListServiceManagementSubscriptions(ref AzureAccount account, AzureEnvironment environment, SecureString password, ShowDialog promptBehavior)
        {
            List<AzureSubscription> result = new List<AzureSubscription>();

            foreach (var tenant in account.GetPropertyAsArray(AzureAccount.Property.Tenants))
            {
                try
                {
                    var tenantToken = AzureSession.AuthenticationFactory.Authenticate(ref account, environment, tenant, password, ShowDialog.Never);

                    using (var subscriptionClient = AzureSession.ClientFactory.CreateCustomClient<WindowsAzure.Subscriptions.SubscriptionClient>(
                            new TokenCloudCredentials(tenantToken.AccessToken),
                            environment.GetEndpointAsUri(AzureEnvironment.Endpoint.ServiceManagement)))
                    {
                        var subscriptionListResult = subscriptionClient.Subscriptions.List();
                        foreach (var subscription in subscriptionListResult.Subscriptions)
                        {
                            AzureSubscription psSubscription = new AzureSubscription
                            {
                                Id = new Guid(subscription.SubscriptionId),
                                Name = subscription.SubscriptionName,
                                Environment = environment.Name
                            };
                            psSubscription.Properties[AzureSubscription.Property.SupportedModes] = AzureModule.AzureServiceManagement.ToString();
                            psSubscription.SetProperty(AzureSubscription.Property.Tenants, subscription.ActiveDirectoryTenantId);

                            AzureSession.SubscriptionTokenCache[Tuple.Create(psSubscription.Id, account.Id)] = tenantToken;

                            result.Add(psSubscription);
                        }
                    }
                }
                catch (CloudException cEx)
                {
                    WriteOrThrowAadExceptionMessage(cEx);
                }
                catch (AadAuthenticationException aadEx)
                {
                    WriteOrThrowAadExceptionMessage(aadEx);
                }
            }

            return result;
        }

        private void WriteOrThrowAadExceptionMessage(AadAuthenticationException aadEx)
        {
            if (aadEx is AadAuthenticationFailedWithoutPopupException)
            {
                WriteDebugMessage(aadEx.Message);
            }
            else if (aadEx is AadAuthenticationCanceledException)
            {
                WriteWarningMessage(aadEx.Message);
            }
            else
            {
                throw aadEx;
            }
        }

        private void WriteOrThrowAadExceptionMessage(CloudException aadEx)
        {
            WriteDebugMessage(aadEx.Message);
        }

        #endregion

        #region Environment management

        public AzureEnvironment GetEnvironmentOrDefault(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return AzureSession.CurrentContext.Environment;
            }
            else if (AzureSession.CurrentContext.Environment != null && AzureSession.CurrentContext.Environment.Name == name)
            {
                return AzureSession.CurrentContext.Environment;
            }
            else if (Profile.Environments.ContainsKey(name))
            {
                return Profile.Environments[name];
            }
            else
            {
                throw new ArgumentException(string.Format(Resources.EnvironmentNotFound, name));
            }
        }

        public List<AzureEnvironment> ListEnvironments(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return Profile.Environments.Values.ToList();
            }
            else if (Profile.Environments.ContainsKey(name))
            {
                return new[] { Profile.Environments[name] }.ToList();
            }
            else
            {
                return new AzureEnvironment[0].ToList();
            }
        }

        public AzureEnvironment RemoveEnvironment(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentNullException("Environment name needs to be specified.", "name");
            }
            if (AzureEnvironment.PublicEnvironments.ContainsKey(name))
            {
                throw new ArgumentException(Resources.RemovingDefaultEnvironmentsNotSupported, "name");
            }
            
            if (Profile.Environments.ContainsKey(name))
            {
                var environment = Profile.Environments[name];
                var subscriptions = Profile.Subscriptions.Values.Where(s => s.Environment == name).ToArray();
                foreach (var subscription in subscriptions)
                {
                    RemoveSubscription(subscription.Id);
                }
                Profile.Environments.Remove(name);
                return environment;
            }
            else
            {
                throw new ArgumentException(string.Format(Resources.EnvironmentNotFound, name), "name");
            }
        }

        public AzureEnvironment AddOrSetEnvironment(AzureEnvironment environment)
        {
            if (environment == null)
            {
                throw new ArgumentNullException("Environment needs to be specified.", "environment");
            }

            if (AzureEnvironment.PublicEnvironments.ContainsKey(environment.Name))
            {
                throw new ArgumentException(Resources.ChangingDefaultEnvironmentNotSupported, "environment");
            }

            if (Profile.Environments.ContainsKey(environment.Name))
            {
                Profile.Environments[environment.Name] =
                    MergeEnvironmentProperties(environment, Profile.Environments[environment.Name]);
            }
            else
            {
                Profile.Environments[environment.Name] = environment;
            }

            // Update in-memory environment
            if (AzureSession.CurrentContext != null && AzureSession.CurrentContext.Environment != null &&
                AzureSession.CurrentContext.Environment.Name == environment.Name)
            {
                AzureSession.SetCurrentContext(AzureSession.CurrentContext.Subscription, 
                    Profile.Environments[environment.Name], 
                    AzureSession.CurrentContext.Account);
            }

            return Profile.Environments[environment.Name];
        }
        #endregion
    }
}