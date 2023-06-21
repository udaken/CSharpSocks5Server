// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Buffers.Binary;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

#pragma warning disable SA1648 // TODO: https://github.com/DotNetAnalyzers/StyleCopAnalyzers/issues/3595

namespace Shim.System.Net
{
    /// <summary>
    /// Represents an IP network with an <see cref="IPAddress"/> containing the network prefix and an <see cref="int"/> defining the prefix length.
    /// </summary>
    /// <remarks>
    /// This type disallows arbitrary IP-address/prefix-length CIDR pairs. <see cref="BaseAddress"/> must be defined so that all bits after the network prefix are set to zero.
    /// In other words, <see cref="BaseAddress"/> is always the first usable address of the network.
    /// The constructor and the parsing methods will throw in case there are non-zero bits after the prefix.
    /// </remarks>
    public readonly struct IPNetwork : IEquatable<IPNetwork>
    {
        private readonly IPAddress? _baseAddress;

        /// <summary>
        /// Gets the <see cref="IPAddress"/> that represents the prefix of the network.
        /// </summary>
        public IPAddress BaseAddress => _baseAddress ?? IPAddress.Any;

        /// <summary>
        /// Gets the length of the network prefix in bits.
        /// </summary>
        public int PrefixLength { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="IPNetwork"/> class with the specified <see cref="IPAddress"/> and prefix length.
        /// </summary>
        /// <param name="baseAddress">The <see cref="IPAddress"/> that represents the prefix of the network.</param>
        /// <param name="prefixLength">The length of the prefix in bits.</param>
        /// <exception cref="ArgumentNullException">The specified <paramref name="baseAddress"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The specified <paramref name="prefixLength"/> is smaller than `0` or longer than maximum length of <paramref name="prefixLength"/>'s <see cref="AddressFamily"/>.</exception>
        /// <exception cref="ArgumentException">The specified <paramref name="baseAddress"/> has non-zero bits after the network prefix.</exception>
        public IPNetwork(IPAddress baseAddress, int prefixLength)
        {
            ArgumentNullException.ThrowIfNull(baseAddress);

            if (prefixLength < 0 || prefixLength > GetMaxPrefixLength(baseAddress))
            {
                ThrowArgumentOutOfRangeException();
            }

            if (HasNonZeroBitsAfterNetworkPrefix(baseAddress, prefixLength))
            {
                ThrowInvalidBaseAddressException();
            }

            _baseAddress = baseAddress;
            PrefixLength = prefixLength;

            [DoesNotReturn]
            static void ThrowArgumentOutOfRangeException() => throw new ArgumentOutOfRangeException(nameof(prefixLength));

            [DoesNotReturn]
            static void ThrowInvalidBaseAddressException() => throw new ArgumentException("SR.net_bad_ip_network_invalid_baseaddress", nameof(baseAddress));
        }

        // Non-validating ctor
        private IPNetwork(IPAddress baseAddress, int prefixLength, bool _)
        {
            _baseAddress = baseAddress;
            PrefixLength = prefixLength;
        }

        /// <summary>
        /// Determines whether a given <see cref="IPAddress"/> is part of the network.
        /// </summary>
        /// <param name="address">The <see cref="IPAddress"/> to check.</param>
        /// <returns><see langword="true"/> if the <see cref="IPAddress"/> is part of the network; otherwise, <see langword="false"/>.</returns>
        /// <exception cref="ArgumentNullException">The specified <paramref name="address"/> is <see langword="null"/>.</exception>
        public bool Contains(IPAddress address)
        {
            ArgumentNullException.ThrowIfNull(address);

            if (address.AddressFamily != BaseAddress.AddressFamily)
            {
                return false;
            }

            // This prevents the 'uint.MaxValue << 32' and the 'UInt128.MaxValue << 128' special cases in the code below.
            if (PrefixLength == 0)
            {
                return true;
            }

            if (address.AddressFamily == AddressFamily.InterNetwork)
            {
                var networkAddress = GetNetworkAddressPart(address, PrefixLength);

                return BaseAddress.Equals(networkAddress);
            }
            else
            {
                throw new NotSupportedException("IPv6 is not supported.");
            }
        }


        private static int GetMaxPrefixLength(IPAddress baseAddress) => baseAddress.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;

        private static bool HasNonZeroBitsAfterNetworkPrefix(IPAddress baseAddress, int prefixLength)
        {
            if (baseAddress.AddressFamily == AddressFamily.InterNetwork)
            {
                // The cast to long ensures that the mask becomes 0 for the case where 'prefixLength == 0'.

                var privateAddress = GetNetworkAddressPart(baseAddress, prefixLength);

                return !privateAddress.Equals(baseAddress);
            }
            else
            {
                throw new NotSupportedException("IPv6 is not supported.");
            }
        }

        /// <summary>
        /// Converts the instance to a string containing the <see cref="IPNetwork"/>'s CIDR notation.
        /// </summary>
        /// <returns>The <see cref="string"/> containing the <see cref="IPNetwork"/>'s CIDR notation.</returns>
        public override string ToString() =>
            string.Create(CultureInfo.InvariantCulture, stackalloc char[128], $"{BaseAddress}/{(uint)PrefixLength}");

        /// <summary>
        /// Determines whether two <see cref="IPNetwork"/> instances are equal.
        /// </summary>
        /// <param name="other">The <see cref="IPNetwork"/> instance to compare to this instance.</param>
        /// <returns><see langword="true"/> if the networks are equal; otherwise <see langword="false"/>.</returns>
        /// <exception cref="InvalidOperationException">Uninitialized <see cref="IPNetwork"/> instance.</exception>
        public bool Equals(IPNetwork other) =>
            PrefixLength == other.PrefixLength &&
            BaseAddress.Equals(other.BaseAddress);

        /// <summary>
        /// Determines whether two <see cref="IPNetwork"/> instances are equal.
        /// </summary>
        /// <param name="obj">The <see cref="IPNetwork"/> instance to compare to this instance.</param>
        /// <returns><see langword="true"/> if <paramref name="obj"/> is an <see cref="IPNetwork"/> instance and the networks are equal; otherwise <see langword="false"/>.</returns>
        /// <exception cref="InvalidOperationException">Uninitialized <see cref="IPNetwork"/> instance.</exception>
        public override bool Equals([NotNullWhen(true)] object? obj) =>
            obj is IPNetwork other &&
            Equals(other);

        /// <summary>
        /// Determines whether the specified instances of <see cref="IPNetwork"/> are equal.
        /// </summary>
        /// <param name="left"></param>
        /// <param name="right"></param>
        /// <returns><see langword="true"/> if the networks are equal; otherwise <see langword="false"/>.</returns>
        public static bool operator ==(IPNetwork left, IPNetwork right) => left.Equals(right);

        /// <summary>
        /// Determines whether the specified instances of <see cref="IPNetwork"/> are not equal.
        /// </summary>
        /// <param name="left"></param>
        /// <param name="right"></param>
        /// <returns><see langword="true"/> if the networks are not equal; otherwise <see langword="false"/>.</returns>
        public static bool operator !=(IPNetwork left, IPNetwork right) => !(left == right);

        /// <summary>
        /// Returns the hash code for this instance.
        /// </summary>
        /// <returns>An integer hash value.</returns>
        public override int GetHashCode() => HashCode.Combine(BaseAddress, PrefixLength);


        private static IPAddress GetNetworkAddressPart(IPAddress address, int prefixLength)
        {
            if (address.AddressFamily == AddressFamily.InterNetwork)
            {
                uint mask = (uint)((long)uint.MaxValue << (32 - prefixLength));
                if (BitConverter.IsLittleEndian)
                {
                    mask = BinaryPrimitives.ReverseEndianness(mask);
                }

                return new IPAddress(address.Address & mask);
            }
            else
            {
                throw new NotSupportedException("IPv6 is not supported.");
            }

        }

        public static IPNetwork? FindIPV4AddressInNetworkInterfaces(IPAddress ipaddress)
        {
            var ipInfo = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up)
                .SelectMany(adaptor => adaptor.GetIPProperties().UnicastAddresses)
                .FirstOrDefault(address => address.Address.Equals(ipaddress));
            if (ipInfo != null)
            {
                return new IPNetwork(GetNetworkAddressPart(ipInfo.Address, ipInfo.PrefixLength), ipInfo.PrefixLength);
            }
            return null;
        }
    }

}
